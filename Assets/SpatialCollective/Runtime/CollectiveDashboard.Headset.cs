using UnityEngine;
using UnityEngine.XR;

namespace SpatialCollective
{
    /// <summary>Participant workspace presentation: automatic, desktop or headset.</summary>
    public enum CollectivePresentation { Automatic, Desktop, Headset }
    /// <summary>Participant workspace portion of the dashboard, providing read-only views without buttons or input colliders.</summary>
    public sealed partial class CollectiveDashboard
    {
        [Header("Always-visible participant workspace")]
        public CollectivePresentation presentation=CollectivePresentation.Automatic;
        [Range(.3f,.85f)] public float panelOpacity=.64f;
        [Range(.8f,1f)] public float viewportOpacity=.94f;
        [Range(1.5f,2.5f)] public float viewingDistance=2f;
        private TextMesh valueReadout,workspaceTitle,overviewCaption,detailCaption,detailNote,navigationHint;
        private Transform workspaceRoot;
        private Mesh workspaceQuadMesh;
        private Renderer overviewFrame,detailFrame,valueFrame,titleFrame;
        private float lastAspect=-1,lastFov=-1;
        private bool lastHeadset;
        public const float ReadoutWidth=.78f,ReadoutHeight=.19f;
        public bool IsHeadsetPresentation=>presentation==CollectivePresentation.Headset ||
            (presentation==CollectivePresentation.Automatic&&(XRSettings.isDeviceActive||Application.platform==RuntimePlatform.Android));
        public bool ParticipantViewsVisible=>HeadsetLayout&&workspaceRoot!=null&&workspaceRoot.gameObject.activeInHierarchy;

        /// <summary>Build participant overview, detail and parameter panels; disable engineering controls and colliders.</summary>
        public void ConfigureHeadsetLayout()
        {
            if(HeadsetLayout)return;
            // Reuse the authored screen mesh. UI surfaces do not need physics components.
            // CreatePrimitive depends on collider types surviving Android engine stripping.
            var source = overviewScreen != null ? overviewScreen.GetComponent<MeshFilter>() : null;
            if(source == null || source.sharedMesh == null)
                throw new System.InvalidOperationException("The authored whole-view quad mesh is missing.");
            workspaceQuadMesh = source.sharedMesh;
            HideRehearsal();
            foreach(Transform child in transform)child.gameObject.SetActive(false);
            foreach(var collider in GetComponentsInChildren<Collider>(true))collider.enabled=false;
            workspaceRoot=Group("Participant three-view workspace");
            workspaceRoot.gameObject.SetActive(false);
            var glass=new Material(experience.uiMaterial){name="Participant translucent glass",color=new Color(.025f,.045f,.055f,panelOpacity),renderQueue=3500};owned.Add(glass);
            overviewFrame=WorkspaceQuad("Whole view glass frame",glass);
            detailFrame=WorkspaceQuad("Detail view glass frame",glass);
            valueFrame=WorkspaceQuad("Read-only parameter glass",glass);
            titleFrame=WorkspaceQuad("Location glass",glass);
            overviewScreen=WorkspaceQuad("Whole habitat - live Mars view",TextureMaterial(experience.linkedViews.OverviewTexture));
            roomScreen=WorkspaceQuad("Selected space - live detail view",TextureMaterial(experience.linkedViews.RoomTexture));
            overviewScreen.sharedMaterial.SetFloat("_Opacity",viewportOpacity);roomScreen.sharedMaterial.SetFloat("_Opacity",viewportOpacity);
            workspaceTitle=WorkspaceLabel("Current space",Ink);overviewCaption=WorkspaceLabel("Whole caption",Cyan);
            detailCaption=WorkspaceLabel("Detail caption",Cyan);detailNote=WorkspaceLabel("Detail parameters",Ink);valueReadout=WorkspaceLabel("Current values",Ink);
            navigationHint=WorkspaceLabel("Gesture navigation status",Ink);
            overviewCaption.text="WHOLE HABITAT / WHITE = CURRENT";
            // Publish readiness only after every view and label exists.
            HeadsetLayout=true;workspaceRoot.gameObject.SetActive(true);
            RecenterWorkspace();RefreshReadout();
            Debug.Log("[SpatialCollective] Three-view workspace initialized; read-only parameters; headset="+IsHeadsetPresentation);
        }
        /// <summary>Project a ray aimed at the overview screen into its camera without a UI collider.</summary>
        /// <param name="pointer">World-space hand pointing ray.</param>
        /// <param name="modelRay">Output model ray from the overview camera.</param>
        /// <param name="hit">Output world-space intersection with the overview plane.</param>
        /// <returns>True when the ray hits the visible overview screen.</returns>
        public bool TryOverviewRay(Ray pointer,out Ray modelRay,out Vector3 hit)
        {
            modelRay=default;hit=default;
            if(!ParticipantViewsVisible||overviewScreen==null||!overviewScreen.gameObject.activeInHierarchy||
                experience.linkedViews.OverviewCamera==null)return false;
            var screen=overviewScreen.transform;
            var plane=new Plane(screen.forward,screen.position);
            if(!plane.Raycast(pointer,out float distance)||distance<=0)return false;
            hit=pointer.GetPoint(distance);
            Vector3 local=screen.InverseTransformPoint(hit);
            if(Mathf.Abs(local.x)>.5f||Mathf.Abs(local.y)>.5f)return false;
            modelRay=experience.linkedViews.OverviewCamera.ViewportPointToRay(new Vector3(local.x+.5f,local.y+.5f,0));
            return true;
        }
        /// <summary>Reuse the plane mesh for a workspace surface without a collider.</summary>
        /// <param name="name">Object name.</param>
        /// <param name="material">Surface material.</param>
        private Renderer WorkspaceQuad(string name,Material material)
        {
            var go=new GameObject(name);go.layer=9;go.transform.SetParent(workspaceRoot,false);
            go.AddComponent<MeshFilter>().sharedMesh=workspaceQuadMesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            return renderer;
        }
        /// <summary>Check read-only surface hits to prevent selecting models behind the panel.</summary>
        /// <param name="ray">World-space pointing ray.</param>
        /// <returns>True when a visible read-only surface is hit.</returns>
        public bool IsReadOnlySurface(Ray ray)
        {
            if(!ParticipantViewsVisible)return false;
            foreach(var surface in new[]{roomScreen,valueFrame,titleFrame,overviewFrame,detailFrame})
            {
                if(surface==null||!surface.gameObject.activeInHierarchy)continue;
                var t=surface.transform;var plane=new Plane(t.forward,t.position);
                if(!plane.Raycast(ray,out float distance)||distance<=0)continue;
                var local=t.InverseTransformPoint(ray.GetPoint(distance));
                if(Mathf.Abs(local.x)<=.5f&&Mathf.Abs(local.y)<=.5f)return true;
            }
            return false;
        }
        /// <summary>Check whether the user faces a panel to avoid rotating the workspace while reading.</summary>
        /// <param name="surface">Panel surface to inspect.</param>
        private bool Reading(Renderer surface)
        {
            var head=experience.head.transform;
            return surface!=null&&Vector3.Dot(head.forward,(surface.transform.position-head.position).normalized)>.94f;
        }
        /// <summary>Create top-left-aligned workspace text.</summary>
        /// <param name="name">Text object name.</param>
        /// <param name="color">Text colour.</param>
        private TextMesh WorkspaceLabel(string name,Color color)
        {var label=Label(name,"",0,0,10,color,TextAnchor.UpperLeft,workspaceRoot);label.lineSpacing=1.15f;return label;}
        /// <summary>Set a workspace plane's local position and dimensions.</summary>
        /// <param name="renderer">Plane renderer to lay out.</param>
        /// <param name="x">Local horizontal centre.</param>
        /// <param name="y">Local vertical centre.</param>
        /// <param name="width">Local width.</param>
        /// <param name="height">Local height.</param>
        /// <param name="z">Local depth.</param>
        private static void Quad(Renderer renderer,float x,float y,float width,float height,float z=-.004f)
        {renderer.transform.localPosition=new Vector3(x,y,z);renderer.transform.localScale=new Vector3(width,height,1);}
        /// <summary>Set workspace text position and size.</summary>
        /// <param name="label">Label to lay out.</param>
        /// <param name="x">Local horizontal coordinate.</param>
        /// <param name="y">Local vertical coordinate.</param>
        /// <param name="height">Reference character height.</param>
        private static void Text(TextMesh label,float x,float y,float height)
        {label.transform.localPosition=new Vector3(x,y,-.018f);label.characterSize=height*10/64;}
        /// <summary>Immediately realign the participant workspace to the head and rebuild its layout.</summary>
        public void RecenterWorkspace(){if(!HeadsetLayout||workspaceRoot==null)return;PositionWorkspace(true);LayoutWorkspace();}
        /// <summary>Follow head position; in a headset, smoothly follow yaw only when the user is neither operating nor reading.</summary>
        /// <param name="immediate">True aligns rotation immediately.</param>
        private void PositionWorkspace(bool immediate)
        {
            var head=experience.head.transform;
            Quaternion target=IsHeadsetPresentation?Quaternion.Euler(0,head.eulerAngles.y,0):head.rotation;
            if(immediate||!IsHeadsetPresentation)transform.rotation=target;
            else if(Quaternion.Angle(transform.rotation,target)>15 && !experience.IsDragging && !experience.SpatialPointerOnOverview &&
                !Reading(overviewScreen)&&!Reading(roomScreen)&&!Reading(valueFrame)&&!Reading(titleFrame))
            {
                float delta=Mathf.DeltaAngle(transform.eulerAngles.y,head.eulerAngles.y);
                float yaw=head.eulerAngles.y-Mathf.Sign(delta)*15;
                transform.rotation=Quaternion.Euler(0,Mathf.MoveTowardsAngle(transform.eulerAngles.y,yaw,90*Time.unscaledDeltaTime),0);
            }
            // The UI follows translation and settles its yaw; the habitat and XR Origin stay independent.
            transform.position=head.position+transform.forward*viewingDistance;transform.localScale=Vector3.one;
        }
        /// <summary>Compute overview, detail, position and parameter-card layouts from the headset or desktop field of view.</summary>
        public void LayoutWorkspace()
        {
            if(!HeadsetLayout||workspaceRoot==null)return;
            lastAspect=experience.head.aspect;lastFov=experience.head.fieldOfView;lastHeadset=IsHeadsetPresentation;
            if(lastHeadset)
            {
                const float x=.96f,w=.78f,h=.43f;
                Quad(overviewFrame,x,.34f,w+.025f,h+.085f);Quad(overviewScreen,x,.31f,w,h,-.008f);Text(overviewCaption,x-w/2,.585f,.036f);
                Quad(detailFrame,x,-.22f,w+.025f,h+.10f);Quad(roomScreen,x,-.235f,w,h,-.008f);Text(detailCaption,x-w/2,.027f,.033f);Text(detailNote,x-w/2,-.467f,.029f);
                Quad(valueFrame,x,-.64f,ReadoutWidth+.025f,ReadoutHeight);Text(valueReadout,x-w/2,-.566f,.040f);
                Quad(titleFrame,-.20f,.55f,.88f,.13f);Text(workspaceTitle,-.62f,.599f,.030f);Text(navigationHint,-.62f,.548f,.027f);
            }
            else
            {
                float height=2*viewingDistance*Mathf.Tan(experience.head.fieldOfView*Mathf.Deg2Rad*.5f),width=height*experience.head.aspect;
                float x=width*.322f,w=Mathf.Min(width*.316f,height*.295f*(16f/9)),h=w/(16f/9),y1=height*.283f,y2=-height*.079f;
                Quad(overviewFrame,x,y1+.012f*height,w+.018f*width,h+.065f*height);Quad(overviewScreen,x,y1,w,h,-.008f);Text(overviewCaption,x-w/2,y1+h/2+.043f*height,.019f*height);
                Quad(detailFrame,x,y2+.004f*height,w+.018f*width,h+.088f*height);Quad(roomScreen,x,y2,w,h,-.008f);Text(detailCaption,x-w/2,y2+h/2+.043f*height,.019f*height);Text(detailNote,x-w/2,y2-h/2-.013f*height,.0145f*height);
                Quad(valueFrame,x,-.375f*height,w+.018f*width,.16f*height);Text(valueReadout,x-w/2,-.310f*height,.023f*height);
                Quad(titleFrame,-.215f*width,.435f*height,.53f*width,.09f*height);Text(workspaceTitle,-.472f*width,.468f*height,.024f*height);Text(navigationHint,-.472f*width,.429f*height,.018f*height);
            }
        }
        /// <summary>Update workspace placement and relayout when field of view or presentation changes.</summary>
        private void LateUpdate()
        {
            if(!ParticipantViewsVisible)return;PositionWorkspace(false);
            if(Mathf.Abs(lastAspect-experience.head.aspect)>.001f||Mathf.Abs(lastFov-experience.head.fieldOfView)>.01f||lastHeadset!=IsHeadsetPresentation)LayoutWorkspace();
        }
        /// <summary>Update occupied cabin, target, gesture hints and selected-region read-only values.</summary>
        private void RefreshReadout()
        {
            if(valueReadout==null)return;var r=experience.rig;int selected=experience.selectedRegion;
            string target=experience.InspectionNode>=0?r.data.nodes[experience.InspectionNode].id:r.data.nodes[r.selectedNode].id;
            workspaceTitle.text=(r.inside?"INSIDE "+r.data.nodes[r.selectedNode].id:"OVERVIEW")+" | TARGET "+target;
            navigationHint.text=experience.SpatialHint;
            detailCaption.text=experience.linkedViews.DetailTitle;detailNote.text=experience.linkedViews.DetailNote;
            if(selected==0)valueReadout.text=$"WHOLE  p {r.GetBaseline(0):0.00}\nLONG {r.Actual[1]:0.00}   A {r.Actual[2]:0.00}   B {r.Actual[3]:0.00}\nLINK {r.GetCoupling(0):+0.00;-0.00;0.00}  {r.GetCoupling(1):+0.00;-0.00;0.00}  {r.GetCoupling(2):+0.00;-0.00;0.00}";
            else if(selected<4)valueReadout.text=$"{CollectiveRig.RegionNames[selected]}\np {r.Actual[selected]:0.00}   BASE {r.GetBaseline(selected):0.00}\nLINK {r.GetCoupling(selected-1):+0.00;-0.00;0.00}";
            else valueReadout.text=$"{(selected==4?"S08 / DEPARTURE":"O13 / APPROACH")}\nSHAPE {r.GetBaseline(selected):0.00}\nTRAVEL {r.GetTravel(selected-4):0.00}";
        }
    }
}
