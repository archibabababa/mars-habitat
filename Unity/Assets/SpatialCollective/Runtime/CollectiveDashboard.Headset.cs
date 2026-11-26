using UnityEngine;
using UnityEngine.XR;

namespace SpatialCollective
{
    public enum CollectivePresentation { Automatic, Desktop, Headset }
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
        private Renderer overviewFrame,detailFrame,valueFrame;
        private float lastAspect=-1,lastFov=-1;
        private bool lastHeadset;
        public const float ReadoutWidth=1f,ReadoutHeight=.40f;
        public bool IsHeadsetPresentation=>presentation==CollectivePresentation.Headset ||
            (presentation==CollectivePresentation.Automatic&&(XRSettings.isDeviceActive||Application.platform==RuntimePlatform.Android));
        public bool ParticipantViewsVisible=>HeadsetLayout&&workspaceRoot!=null&&workspaceRoot.gameObject.activeInHierarchy;
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
        private Renderer WorkspaceQuad(string name,Material material)
        {
            var go=new GameObject(name);go.layer=9;go.transform.SetParent(workspaceRoot,false);
            go.AddComponent<MeshFilter>().sharedMesh=workspaceQuadMesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            return renderer;
        }
        public bool IsReadOnlySurface(Ray ray)
        {
            if(!ParticipantViewsVisible)return false;
            foreach(var surface in new[]{roomScreen,valueFrame,overviewFrame,detailFrame})
            {
                if(surface==null||!surface.gameObject.activeInHierarchy)continue;
                var t=surface.transform;var plane=new Plane(t.forward,t.position);
                if(!plane.Raycast(ray,out float distance)||distance<=0)continue;
                var local=t.InverseTransformPoint(ray.GetPoint(distance));
                if(Mathf.Abs(local.x)<=.5f&&Mathf.Abs(local.y)<=.5f)return true;
            }
            return false;
        }
        private bool Reading(Renderer surface)
        {
            var head=experience.head.transform;
            return surface!=null&&Vector3.Dot(head.forward,(surface.transform.position-head.position).normalized)>.94f;
        }
        private TextMesh WorkspaceLabel(string name,Color color)
        {var label=Label(name,"",0,0,10,color,TextAnchor.UpperLeft,workspaceRoot);label.lineSpacing=1.15f;return label;}
        private static void Quad(Renderer renderer,float x,float y,float width,float height,float z=-.004f)
        {renderer.transform.localPosition=new Vector3(x,y,z);renderer.transform.localScale=new Vector3(width,height,1);}
        private static void Text(TextMesh label,float x,float y,float height)
        {label.transform.localPosition=new Vector3(x,y,-.018f);label.characterSize=height*10/64;}
        public void RecenterWorkspace(){if(!HeadsetLayout||workspaceRoot==null)return;PositionWorkspace(true);LayoutWorkspace();}
        private void PositionWorkspace(bool immediate)
        {
            var head=experience.head.transform;
            Quaternion target=IsHeadsetPresentation?Quaternion.Euler(0,head.eulerAngles.y,0):head.rotation;
            if(immediate||!IsHeadsetPresentation)transform.rotation=target;
            else if(Quaternion.Angle(transform.rotation,target)>15 && !experience.IsDragging && !experience.SpatialPointerOnOverview &&
                !Reading(overviewScreen)&&!Reading(roomScreen)&&!Reading(valueFrame))
            {
                float delta=Mathf.DeltaAngle(transform.eulerAngles.y,head.eulerAngles.y);
                float yaw=head.eulerAngles.y-Mathf.Sign(delta)*15;
                transform.rotation=Quaternion.Euler(0,Mathf.MoveTowardsAngle(transform.eulerAngles.y,yaw,90*Time.unscaledDeltaTime),0);
            }
            // The UI follows translation and settles its yaw; the habitat and XR Origin stay independent.
            transform.position=head.position+transform.forward*viewingDistance;transform.localScale=Vector3.one;
        }
        public void LayoutWorkspace()
        {
            if(!HeadsetLayout||workspaceRoot==null)return;
            lastAspect=experience.head.aspect;lastFov=experience.head.fieldOfView;lastHeadset=IsHeadsetPresentation;
            if(lastHeadset)
            {
                const float x=1.08f,w=1f,h=.5625f;
                Quad(overviewFrame,x,.385f,w+.025f,h+.085f);Quad(overviewScreen,x,.36f,w,h,-.008f);Text(overviewCaption,x-w/2,.705f,.036f);
                Quad(detailFrame,x,-.30f,w+.025f,h+.08f);Quad(roomScreen,x,-.32f,w,h,-.008f);Text(detailCaption,x-w/2,.017f,.033f);
                Quad(valueFrame,x,-.87f,ReadoutWidth+.025f,ReadoutHeight);
                Text(workspaceTitle,x-w/2,-.685f,.029f);Text(valueReadout,x-w/2,-.74f,.033f);
                Text(detailNote,x-w/2,-.88f,.025f);Text(navigationHint,x-w/2,-.925f,.024f);
            }
            else
            {
                float height=2*viewingDistance*Mathf.Tan(experience.head.fieldOfView*Mathf.Deg2Rad*.5f),width=height*experience.head.aspect;
                float x=width*.305f,w=Mathf.Min(width*.36f,height*.315f*(16f/9)),h=w/(16f/9),y1=height*.285f,y2=-height*.10f;
                Quad(overviewFrame,x,y1+.012f*height,w+.018f*width,h+.06f*height);Quad(overviewScreen,x,y1,w,h,-.008f);Text(overviewCaption,x-w/2,y1+h/2+.033f*height,.0185f*height);
                Quad(detailFrame,x,y2+.012f*height,w+.018f*width,h+.06f*height);Quad(roomScreen,x,y2,w,h,-.008f);Text(detailCaption,x-w/2,y2+h/2+.033f*height,.0185f*height);
                Quad(valueFrame,x,-.39f*height,w+.018f*width,.19f*height);
                Text(workspaceTitle,x-w/2,-.307f*height,.0155f*height);Text(valueReadout,x-w/2,-.333f*height,.0185f*height);
                Text(detailNote,x-w/2,-.408f*height,.014f*height);Text(navigationHint,x-w/2,-.434f*height,.0135f*height);
            }
        }
        private void LateUpdate()
        {
            if(!ParticipantViewsVisible)return;PositionWorkspace(false);
            if(Mathf.Abs(lastAspect-experience.head.aspect)>.001f||Mathf.Abs(lastFov-experience.head.fieldOfView)>.01f||lastHeadset!=IsHeadsetPresentation)LayoutWorkspace();
        }
        private void RefreshReadout()
        {
            if(valueReadout==null)return;var r=experience.rig;int selected=experience.selectedRegion;
            string target=experience.InspectionNode>=0?r.data.nodes[experience.InspectionNode].id:r.data.nodes[r.selectedNode].id;
            workspaceTitle.text=(r.inside?"INSIDE "+r.data.nodes[r.selectedNode].id:"OVERVIEW")+" | TARGET "+target;
            navigationHint.text=experience.SpatialHint.Replace(" / BOTH:","\nBOTH:");
            detailCaption.text=experience.linkedViews.DetailTitle;detailNote.text="VIEW "+experience.linkedViews.RoomId+" | "+experience.linkedViews.DetailNote;
            if(selected==0)valueReadout.text=$"WHOLE  p {r.GetBaseline(0):0.00}\nLONG {r.Actual[1]:0.00}   A {r.Actual[2]:0.00}   B {r.Actual[3]:0.00}\nLINK {r.GetCoupling(0):+0.00;-0.00;0.00}  {r.GetCoupling(1):+0.00;-0.00;0.00}  {r.GetCoupling(2):+0.00;-0.00;0.00}";
            else if(selected<4)valueReadout.text=$"{CollectiveRig.RegionNames[selected]}\np {r.Actual[selected]:0.00}   BASE {r.GetBaseline(selected):0.00}\nLINK {r.GetCoupling(selected-1):+0.00;-0.00;0.00}";
            else valueReadout.text=$"{(selected==4?"S08 / DEPARTURE":"O13 / APPROACH")}\nSHAPE {r.GetBaseline(selected):0.00}\nTRAVEL {r.GetTravel(selected-4):0.00}";
        }
    }
}
