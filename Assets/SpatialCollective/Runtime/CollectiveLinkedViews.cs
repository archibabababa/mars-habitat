using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Maintain offscreen overview and cabin-detail cameras, render textures, and detached-cabin path guides.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CollectiveLinkedViews : MonoBehaviour
    {
        public CollectiveRig rig;
        public Camera head;
        public Transform trackingOrigin;
        public Material marsSky;
        public Transform panelRoot;
        public CollectiveDashboard dashboard;
        public float refreshRate=10;
        public Camera OverviewCamera {get;private set;}
        public Camera RoomCamera {get;private set;}
        public RenderTexture OverviewTexture {get;private set;}
        public RenderTexture RoomTexture {get;private set;}
        public int RenderCount {get;private set;}
        public string RoomId {get;private set;}
        public string DetailTitle {get;private set;}="LINKED ROOM";
        public string DetailNote {get;private set;}="BODY MOTION 1:1";
        public bool DetailIsRoute {get;private set;}
        private int lastDetailNode=-1;
        private readonly float[] detachedRadius={3,3};
        private readonly float[] detailSize=new float[2];
        private readonly Vector3[] detailOffset=new Vector3[2];
        private Vector3 routePosition;
        private float routeSize;
        private readonly Quaternion detailRotation=Quaternion.LookRotation(new Vector3(-.7f,-.35f,1));
        private Vector3 referencePosition,overviewPosition;
        private Quaternion referenceRotation,overviewRotation;
        private float nextRender,overviewSize;
        private readonly LineRenderer[] trajectories=new LineRenderer[2];
        private readonly Material[] trajectoryMaterials=new Material[2];
        /// <summary>Create offscreen cameras, textures, and path lines, calculate stable framing, and bind them to the panel.</summary>
        public void Initialize()
        {
            OverviewTexture=new RenderTexture(640,360,16,RenderTextureFormat.ARGB32){name="Collective live overview"};
            RoomTexture=new RenderTexture(640,360,16,RenderTextureFormat.ARGB32){name="Linked room live view"};
            OverviewTexture.Create();RoomTexture.Create();
            OverviewCamera=MakeCamera("Collective context camera",OverviewTexture,true);
            RoomCamera=MakeCamera("Linked room camera",RoomTexture,false);
            OverviewCamera.gameObject.AddComponent<CollectiveViewFilter>().rig=rig;
            OverviewCamera.GetComponent<CollectiveViewFilter>().overview=true;
            RoomCamera.gameObject.AddComponent<CollectiveViewFilter>().rig=rig;
            RoomCamera.GetComponent<CollectiveViewFilter>().followSelected=false;
            RoomCamera.gameObject.AddComponent<Skybox>();
            OverviewCamera.cullingMask=(1<<8)|(1<<10);OverviewCamera.clearFlags=CameraClearFlags.Skybox;
            OverviewCamera.gameObject.AddComponent<Skybox>().material=marsSky;
            FitStableOverview();
            for(int i=0;i<2;i++)
            {
                var go=new GameObject(i==0?"S08 departure guide":"O13 approach guide");go.layer=10;
                var line=go.AddComponent<LineRenderer>();line.positionCount=33;line.useWorldSpace=true;
                var material=new Material(Shader.Find("SpatialCollective/SpatialTint"));material.color=i==0?new Color(1,.68f,.28f,.45f):new Color(.65f,.65f,1,.4f);
                line.sharedMaterial=material;trajectories[i]=line;trajectoryMaterials[i]=material;
            }
            dashboard.BindViews(this);Calibrate();
        }
        /// <summary>Create a monocular camera rendered manually by code.</summary>
        /// <param name="name">Name of the new camera object.</param>
        /// <param name="target">Render texture receiving the camera output.</param>
        /// <param name="orthographic">Whether to use orthographic projection.</param>
        /// <returns>Configured camera with automatic rendering disabled.</returns>
        private Camera MakeCamera(string name,RenderTexture target,bool orthographic)
        {
            var c=new GameObject(name).AddComponent<Camera>();c.enabled=false;c.stereoTargetEye=StereoTargetEyeMask.None;
            c.orthographic=orthographic;c.targetTexture=target;c.cullingMask=1<<8;
            c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.04f,.11f,.14f,1);
            c.allowHDR=false;c.allowMSAA=false;c.fieldOfView=85;return c;
        }
        /// <summary>Sample deformation and path combinations to estimate fixed framing, then restore the previous model state.</summary>
        private void FitStableOverview()
        {
            var basis=new Vector3[rig.data.batches.Length][];
            for(int b=0;b<basis.Length;b++)basis[b]=rig.data.batches[b].mesh.vertices;
            // Fixed framing preserves visible expansion/folding; use actual vertices, not merged AABB corners.
            Vector3 aim=rig.data.overviewCenter+Vector3.up*6;
            overviewRotation=Quaternion.LookRotation(-new Vector3(30,28,-105));
            Quaternion inverse=Quaternion.Inverse(overviewRotation);
            Vector3 min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity;
            float[] shape=new float[6];for(int i=0;i<6;i++)shape[i]=rig.GetBaseline(i);
            float dep=rig.departureProgress,app=rig.approachProgress;bool linked=rig.coupled;
            rig.coupled=false;
            for(int step=0;step<5;step++)for(int travel=0;travel<4;travel++)
            {
                for(int i=0;i<6;i++)rig.SetBaseline(i,step*.25f);
                rig.SetTravel(0,travel&1);rig.SetTravel(1,(travel>>1)&1);rig.EvaluatePose();
                for(int b=0;b<rig.filters.Length;b++)
                {
                    if(!rig.renderers[b].enabled)continue;
                    for(int j=0;j<basis[b].Length;j++)
                    {
                        Vector3 vertex=rig.Matrices[rig.data.batches[b].vertexDrivers[j]].MultiplyPoint3x4(basis[b][j]);
                        Vector3 v=inverse*(vertex-aim);min=Vector3.Min(min,v);max=Vector3.Max(max,v);
                    }
                }
            }
            for(int i=1;i<6;i++)rig.SetBaseline(i,shape[i]);rig.coupled=true;
            for(int step=0;step<=20;step++)for(int travel=0;travel<4;travel++)
            {
                rig.SetBaseline(0,step/20f);rig.SetTravel(0,travel&1);rig.SetTravel(1,(travel>>1)&1);rig.EvaluatePose();
                for(int b=0;b<rig.filters.Length;b++)
                {
                    if(!rig.renderers[b].enabled)continue;
                    for(int j=0;j<basis[b].Length;j++)
                    {
                        Vector3 vertex=rig.Matrices[rig.data.batches[b].vertexDrivers[j]].MultiplyPoint3x4(basis[b][j]);
                        Vector3 v=inverse*(vertex-aim);min=Vector3.Min(min,v);max=Vector3.Max(max,v);
                    }
                }
            }
            // Projected shape envelope: fixed close-up magnification preserves visible deformation.
            var detailMin=new[]{Vector3.one*float.PositiveInfinity,Vector3.one*float.PositiveInfinity};
            var detailMax=new[]{Vector3.one*float.NegativeInfinity,Vector3.one*float.NegativeInfinity};
            Quaternion detailInverse=Quaternion.Inverse(detailRotation);
            for(int step=0;step<=8;step++)
            {
                rig.SetBaseline(4,step/8f);rig.SetBaseline(5,step/8f);rig.EvaluatePose();
                for(int b=0;b<rig.filters.Length;b++)
                {
                    for(int j=0;j<basis[b].Length;j++)
                    {
                        int node=rig.data.drivers[rig.data.batches[b].vertexDrivers[j]].node;
                        if(!rig.data.nodes[node].detached)continue;
                        int f=rig.RegionForNode(node)-4;
                        Vector3 vertex=rig.Matrices[rig.data.batches[b].vertexDrivers[j]].MultiplyPoint3x4(basis[b][j]);
                        detachedRadius[f]=Mathf.Max(detachedRadius[f],Vector3.Distance(vertex,rig.NodeCenter(node)));
                        Vector3 projected=detailInverse*(vertex-rig.NodeCenter(node));
                        detailMin[f]=Vector3.Min(detailMin[f],projected);detailMax[f]=Vector3.Max(detailMax[f],projected);
                    }
                }
            }
            for(int f=0;f<2;f++)
            {
                Vector3 centerDetail=(detailMin[f]+detailMax[f])*.5f,extent=detailMax[f]-detailMin[f];
                detailOffset[f]=detailRotation*new Vector3(centerDetail.x,centerDetail.y,detailMin[f].z-30);
                detailSize[f]=Mathf.Max(extent.y*.5f,extent.x*.5f/(RoomTexture.width/(float)RoomTexture.height))*1.10f;
            }
            for(int i=0;i<6;i++)rig.SetBaseline(i,shape[i]);rig.SetTravel(0,dep);rig.SetTravel(1,app);rig.coupled=linked;rig.Rebuild();
            Vector3 center=(min+max)*.5f;
            overviewPosition=aim+overviewRotation*new Vector3(center.x,center.y,min.z-30);
            overviewSize=Mathf.Max((max.y-min.y)*.5f,(max.x-min.x)*.5f/(OverviewTexture.width/(float)OverviewTexture.height))*1.15f;
        }
        /// <summary>Record a reference head pose so the interior detail camera follows subsequent body movement.</summary>
        public void Calibrate(){referencePosition=head.transform.position;referenceRotation=head.transform.rotation;nextRender=0;}
        /// <summary>Update overview, paths, and detail camera; regular cabins map head movement, while detached cabins use close-up or path views.</summary>
        public void UpdateViewState()
        {
            // Call chain (Guide section 8): RenderNow() calls this first to update both cameras.
            if(OverviewCamera==null || !rig.Ready)return;
            float s=rig.transform.lossyScale.x;
            // 1) Update the overview camera with fixed framing following the model's world pose.
            OverviewCamera.transform.SetPositionAndRotation(rig.transform.TransformPoint(overviewPosition),rig.transform.rotation*overviewRotation);
            OverviewCamera.orthographicSize=overviewSize*s;
            OverviewCamera.nearClipPlane=Mathf.Max(.002f,.04f*s);OverviewCamera.farClipPlane=350*s;
            for(int i=0;i<2;i++)
            {
                // 2) Refresh both detached-cabin path lines (S08/O13).
                trajectories[i].widthMultiplier=.055f*s;
                for(int j=0;j<33;j++)trajectories[i].SetPosition(j,rig.transform.TransformPoint(rig.SampleFlight(i,j/32f)));
            }
            // 3) Prefer InspectionNode for detail; otherwise use selectedNode.
            int node=dashboard.experience.InspectionNode>=0?dashboard.experience.InspectionNode:rig.selectedNode;
            if(rig.extraViewNode!=node){rig.extraViewNode=node;rig.MarkDirty();rig.Rebuild();}
            RoomId=rig.data.nodes[node].id;
            var filter=RoomCamera.GetComponent<CollectiveViewFilter>();filter.focusNode=node;
            bool detached=rig.data.nodes[node].detached;
            RoomCamera.GetComponent<Skybox>().material=marsSky;
            RoomCamera.clearFlags=CameraClearFlags.Skybox;
            if(detached)PositionDetached(node,s);
            else
            {
                // 4) Regular cabins preserve 1:1 body displacement relative to the pose recorded by Calibrate.
                DetailIsRoute=false;RoomCamera.orthographic=false;RoomCamera.cullingMask=1<<8;filter.neighborVisibility=0;
                int[] adjacent=rig.data.nodes[node].neighbors;
                Vector3 look=adjacent.Length>0?rig.NodeCenter(adjacent[0])-rig.NodeCenter(node):rig.data.overviewCenter-rig.NodeCenter(node);
                look=Vector3.ProjectOnPlane(look,Vector3.up);if(look.sqrMagnitude<.01f)look=Vector3.forward;
                Quaternion baseOrientation=Quaternion.LookRotation(look,Vector3.up);
                Quaternion physicalYaw=Quaternion.Euler(0,referenceRotation.eulerAngles.y,0);
                Vector3 displacement=Quaternion.Inverse(physicalYaw)*(head.transform.position-referencePosition);
                float initialHeight=referencePosition.y-trackingOrigin.position.y;
                Vector3 localEye=rig.NodeFloor(node)+baseOrientation*((Vector3.up*initialHeight+displacement)/s);
                RoomCamera.transform.position=rig.transform.TransformPoint(localEye);
                RoomCamera.transform.rotation=rig.transform.rotation*baseOrientation*Quaternion.Inverse(referenceRotation)*head.transform.rotation;
                DetailTitle=RoomId+" / LINKED ROOM";DetailNote="BODY MOTION 1:1";
            }
            lastDetailNode=node;
            RoomCamera.nearClipPlane=Mathf.Max(.001f,.04f*s);RoomCamera.farClipPlane=300*s;
        }
        /// <summary>Update camera states and render both textures immediately, controlling path visibility for each view.</summary>
        public void RenderNow()
        {
            if(OverviewCamera==null || !rig.Ready)return;
            // Call chain (Guide section 8):
            // UpdateViewState -> Overview.Render -> Room.Render -> dashboard.Refresh。
            UpdateViewState();
            bool detached=rig.data.nodes[lastDetailNode].detached;
            for(int i=0;i<2;i++)trajectories[i].enabled=true;
            OverviewCamera.Render();
            // Show the corresponding detached-cabin trajectory only in path mode to avoid visual clutter.
            for(int i=0;i<2;i++)trajectories[i].enabled=detached&&DetailIsRoute&&i==rig.RegionForNode(lastDetailNode)-4;
            RoomCamera.Render();
            for(int i=0;i<2;i++)trajectories[i].enabled=true;
            RenderCount++;dashboard.Refresh();
        }
        /// <summary>Set an orthographic close-up for a detached cabin; switch to path framing during a travel gesture.</summary>
        /// <param name="node">Index of the detached-cabin node.</param>
        /// <param name="scale">World scale of the model.</param>
        private void PositionDetached(int node,float scale)
        {
            int flight=rig.RegionForNode(node)-4;
            if(lastDetailNode!=node)DetailIsRoute=false;
            string operation=dashboard.experience.SpatialOperation;
            if(operation=="Span")DetailIsRoute=false;
            if(operation=="Travel"&&!DetailIsRoute){FitRoute(flight);DetailIsRoute=true;}
            RoomCamera.orthographic=true;RoomCamera.GetComponent<CollectiveViewFilter>().neighborVisibility=0;
            RoomCamera.cullingMask=DetailIsRoute?(1<<8)|(1<<10):1<<8;
            Vector3 position=DetailIsRoute?routePosition:rig.NodeCenter(node)+detailOffset[flight];
            RoomCamera.transform.SetPositionAndRotation(rig.transform.TransformPoint(position),rig.transform.rotation*detailRotation);
            RoomCamera.orthographicSize=(DetailIsRoute?routeSize:detailSize[flight])*scale;
            DetailTitle=RoomId+(DetailIsRoute?" / ROUTE":" / SHAPE CLOSE-UP");
            DetailNote=$"SHAPE {rig.GetBaseline(flight+4):0.00}    TRAVEL {rig.GetTravel(flight):0.00}";
        }
        /// <summary>Sample the path and include the cabin radius to calculate fixed path framing.</summary>
        /// <param name="flight">Path index: 0 for S08, 1 for O13.</param>
        private void FitRoute(int flight)
        {
            Quaternion inverse=Quaternion.Inverse(detailRotation);
            Vector3 min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity;
            for(int j=0;j<=32;j++)
            {
                Vector3 p=inverse*rig.SampleFlight(flight,j/32f);
                min=Vector3.Min(min,p-Vector3.one*detachedRadius[flight]);max=Vector3.Max(max,p+Vector3.one*detachedRadius[flight]);
            }
            Vector3 center=(min+max)*.5f;
            routePosition=detailRotation*new Vector3(center.x,center.y,min.z-30);
            routeSize=Mathf.Max((max.y-min.y)*.5f,(max.x-min.x)*.5f/(RoomTexture.width/(float)RoomTexture.height))*1.12f;
        }
        /// <summary>Refresh offscreen views at the configured frequency while the panel is visible.</summary>
        private void LateUpdate()
        {
            // Call chain (Guide section 8): throttle RenderNow using refreshRate.
            if(panelRoot==null || !panelRoot.gameObject.activeInHierarchy || (dashboard.HeadsetLayout&&!dashboard.ParticipantViewsVisible) || Time.unscaledTime<nextRender)return;
            nextRender=Time.unscaledTime+1/Mathf.Max(1,refreshRate);RenderNow();
        }
        /// <summary>Release cameras, render textures, path objects, and materials.</summary>
        private void OnDestroy()
        {
            if(OverviewCamera!=null)Destroy(OverviewCamera.gameObject);if(RoomCamera!=null)Destroy(RoomCamera.gameObject);
            if(OverviewTexture!=null){OverviewTexture.Release();Destroy(OverviewTexture);}if(RoomTexture!=null){RoomTexture.Release();Destroy(RoomTexture);}
            for(int i=0;i<2;i++){if(trajectories[i]!=null)Destroy(trajectories[i].gameObject);if(trajectoryMaterials[i]!=null)Destroy(trajectoryMaterials[i]);}
        }
    }
}
