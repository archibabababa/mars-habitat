using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialCollective
{
    [Serializable] public sealed class DetachedPath
    {
        public string nodeId,anchorId;
        public Vector3 startOffset,controlOffset,endOffset;
        public float spinDegrees;
    }
    [Serializable] public sealed class FlightPathProfile
    {
        public int schemaVersion;
        public float duration;
        public DetachedPath[] paths;
    }
    [DefaultExecutionOrder(-100)]
    public sealed class CollectiveRig : MonoBehaviour
    {
        public CollectiveData data;
        public TextAsset flightPathsJson;
        public MeshFilter[] filters;
        public MeshRenderer[] renderers;
        [Range(0,1)] public float core=.5f,longBranch=.5f,shortA=.5f,shortB=.5f,departureShape=.5f,approachShape=.5f;
        public bool coupled;
        [Range(-1,1)] public float longResponse=-.8f,shortAResponse=.8f,shortBResponse=-.55f;
        [Range(0,1)] public float departureProgress,approachProgress;
        public bool animateDetached;
        public bool showAllChambers,inside;
        public int selectedNode;
        [NonSerialized] public int extraViewNode=-1;
        public float deformationUpdatesPerSecond=30;
        public event Action GeometryChanged;
        public float[] Actual {get;private set;}=new float[6];
        public Matrix4x4[] Matrices {get;private set;}
        public bool Ready {get;private set;}
        private Mesh[] meshes;
        private Vector3[][] basis,buffer;
        private float[] nodeActivation;
        private FlightPathProfile flights;
        private readonly int[] flightNodes=new int[2],flightAnchors=new int[2];
        private float nextUpdate;
        private readonly float[] previous=new float[8];
        private bool dirty=true,prevInside,prevRooms;
        private int prevNode=-1;
        private MaterialPropertyBlock tint;
        public static readonly string[] RegionNames={"CORE","LONG BRANCH","SHORT A","SHORT B","S08 SHAPE","O13 SHAPE"};
        private void Awake()
        {
            if(data==null || filters==null || filters.Length!=data.batches.Length || renderers==null || renderers.Length!=data.batches.Length)
                throw new InvalidOperationException("Scene model references are incomplete.");
            ValidateFlights();
            Matrices=new Matrix4x4[data.drivers.Length];nodeActivation=new float[data.nodes.Length];
            for(int i=0;i<nodeActivation.Length;i++)nodeActivation[i]=-1;
            meshes=new Mesh[filters.Length];basis=new Vector3[filters.Length][];buffer=new Vector3[filters.Length][];
            for(int i=0;i<filters.Length;i++)
            {
                meshes[i]=Instantiate(data.batches[i].mesh);meshes[i].name=data.batches[i].mesh.name+"_Runtime";
                meshes[i].MarkDynamic();filters[i].sharedMesh=meshes[i];
                basis[i]=data.batches[i].mesh.vertices;buffer[i]=new Vector3[basis[i].Length];
                var nodeIds=new Vector2[basis[i].Length];
                for(int v=0;v<nodeIds.Length;v++)nodeIds[v]=new Vector2(data.drivers[data.batches[i].vertexDrivers[v]].node,data.batches[i].kind=="floor"?1:0);
                meshes[i].uv2=nodeIds; // Visibility metadata only; imported vertices/drivers remain unchanged.
                renderers[i].shadowCastingMode=ShadowCastingMode.Off;renderers[i].receiveShadows=false;
            }
            tint=new MaterialPropertyBlock();Ready=true;Rebuild();
        }
        private void ValidateFlights()
        {
            if(flightPathsJson==null)throw new InvalidDataException("Assign FlightPaths.json.");
            flights=JsonUtility.FromJson<FlightPathProfile>(flightPathsJson.text);
            if(flights==null || flights.schemaVersion!=1 || flights.paths==null || flights.paths.Length!=2 || !Finite(flights.duration) || flights.duration<=0)
                throw new InvalidDataException("Invalid flight-path configuration.");
            for(int i=0;i<2;i++)
            {
                var p=flights.paths[i];if(p==null)throw new InvalidDataException("Missing flight path.");
                flightNodes[i]=FindNode(p.nodeId);flightAnchors[i]=FindNode(p.anchorId);
                if(flightNodes[i]<0 || flightAnchors[i]<0 || !data.nodes[flightNodes[i]].detached || data.nodes[flightAnchors[i]].detached ||
                    !Finite(p.startOffset)||!Finite(p.controlOffset)||!Finite(p.endOffset)||!Finite(p.spinDegrees))
                    throw new InvalidDataException("Invalid flight path nodes or coordinates.");
            }
            if(flightNodes[0]==flightNodes[1] || flights.paths[0].nodeId!="S08" || flights.paths[1].nodeId!="O13")
                throw new InvalidDataException("Expected separate S08 and O13 paths.");
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        private void Update()
        {
            // Parameter writes (SetBaseline/SetCoupling/SetTravel) -> MarkDirty() -> throttled processing here.
            if(!Ready)return;
            if(animateDetached)
            {
                // Automatic flight: advance both detached cabins using progress normalized by FlightPaths.duration.
                float step=Time.unscaledDeltaTime/flights.duration;
                departureProgress=Mathf.Min(1,departureProgress+step);approachProgress=Mathf.Min(1,approachProgress+step);dirty=true;
                if(departureProgress>=1 && approachProgress>=1)animateDetached=false;
            }
            // Throttle rebuilding with deformationUpdatesPerSecond instead of rebuilding every frame.
            if(Time.unscaledTime<nextUpdate)return;
            nextUpdate=Time.unscaledTime+1/Mathf.Max(1,deformationUpdatesPerSecond);
            EvaluateActivation();
            // Compare with the previous cached state; parameter or view changes mark geometry dirty.
            for(int i=0;i<6;i++)if(Mathf.Abs(Actual[i]-previous[i])>.0001f)dirty=true;
            if(previous[6]!=departureProgress || previous[7]!=approachProgress || prevInside!=inside || prevNode!=selectedNode || prevRooms!=showAllChambers)dirty=true;
            // Actual geometry rebuild entry point.
            if(dirty)Rebuild();
        }
        private void EvaluateActivation()
        {
            Actual[0]=Mathf.Clamp01(core);float delta=Actual[0]-.5f;
            for(int i=1;i<4;i++)Actual[i]=Mathf.Clamp01(GetBaseline(i)+(coupled?GetCoupling(i-1)*delta:0));
            Actual[4]=Mathf.Clamp01(departureShape);Actual[5]=Mathf.Clamp01(approachShape);
        }
        public void EvaluatePose()
        {
            if(!Ready)return;
            // 1) Calculate six regional effective values, then override nodeActivation for detached cabins.
            EvaluateActivation();nodeActivation[flightNodes[0]]=Actual[4];nodeActivation[flightNodes[1]]=Actual[5];
            // 2) Evaluate hierarchical kinematics to obtain each driver's matrix.
            CollectiveKinematics.Evaluate(data,Actual,Matrices,nodeActivation);
            for(int i=0;i<2;i++)
            {
                int n=flightNodes[i];var path=flights.paths[i];float t=Mathf.SmoothStep(0,1,GetTravel(i));
                Vector3 anchor=NodeCenter(flightAnchors[i]);
                // 3) Add detached-cabin path poses: quadratic Bezier translation plus rotation about the Y axis.
                Vector3 pos=anchor+(1-t)*(1-t)*path.startOffset+2*(1-t)*t*path.controlOffset+t*t*path.endOffset;
                Matrix4x4 travel=Matrix4x4.Translate(pos)*Matrix4x4.Rotate(Quaternion.AngleAxis(path.spinDegrees*t,Vector3.up))*Matrix4x4.Translate(-data.nodes[n].center);
                for(int d=0;d<data.drivers.Length;d++)if(data.drivers[d].node==n)Matrices[d]=travel*Matrices[d];
            }
        }
        public void Rebuild()
        {
            if(!Ready)return;
            EvaluatePose();
            for(int b=0;b<meshes.Length;b++)
            {
                var spec=data.batches[b];bool interior=spec.kind=="inner" || spec.kind=="floor";
                // Room visibility conditions control interior/floor rendering to avoid unnecessary drawing.
                bool visible=!interior || showAllChambers || spec.node==selectedNode || spec.node==extraViewNode || IsNeighbor(spec.node);
                renderers[b].enabled=visible;if(!visible)continue;
                // Transform base vertices by driver matrices into runtime buffers.
                for(int v=0;v<buffer[b].Length;v++)buffer[b][v]=Matrices[spec.vertexDrivers[v]].MultiplyPoint3x4(basis[b][v]);
                meshes[b].vertices=buffer[b];meshes[b].RecalculateBounds();
            }
            Array.Copy(Actual,previous,6);previous[6]=departureProgress;previous[7]=approachProgress;
            prevInside=inside;
            prevNode=selectedNode;
            prevRooms=showAllChambers;
            dirty=false;
            GeometryChanged?.Invoke();    // Notify listeners that the mesh has been updated.
        }
        private bool IsNeighbor(int n)
        {if(n<0 || selectedNode<0 || selectedNode>=data.nodes.Length)return false;foreach(int other in data.nodes[selectedNode].neighbors)if(other==n)return true;return false;}
        public int FindNode(string id)=>Array.FindIndex(data.nodes,n=>n.id==id);
        public int RegionForNode(int node)=>node==flightNodes[0]?4:node==flightNodes[1]?5:data.nodes[node].region;
        public int NodeForRegion(int region)
        {
            if(region>=4)return flightNodes[region-4];
            for(int n=0;n<data.nodes.Length;n++)if(!data.nodes[n].detached && data.nodes[n].region==region)return n;
            return 0;
        }
        public Vector3 NodeCenter(int n)=>Matrices[n].MultiplyPoint3x4(data.nodes[n].center);
        public Vector3 NodeFloor(int n)=>Matrices[n].MultiplyPoint3x4(data.nodes[n].floor);
        public void SetBaseline(int region,float value)
        {
            if(region<0||region>=6||!Finite(value))throw new ArgumentOutOfRangeException(nameof(region));
            value=Mathf.Clamp01(value);
            switch(region)
            {
                case 0:core=value;break;
                case 1:longBranch=value;break;
                case 2:shortA=value;break;
                case 3:shortB=value;break;
                case 4:departureShape=value;break;
                case 5:approachShape=value;break;}
            dirty=true;
        }
        public float GetBaseline(int r)=>r==0?core:r==1?longBranch:r==2?shortA:r==3?shortB:r==4?departureShape:r==5?approachShape:throw new ArgumentOutOfRangeException(nameof(r));
        public float GetCoupling(int r)=>r==0?longResponse:r==1?shortAResponse:r==2?shortBResponse:throw new ArgumentOutOfRangeException(nameof(r));
        public void SetCoupling(int r,float v)
        {
            if(r<0||r>2||!Finite(v))throw new ArgumentOutOfRangeException(nameof(r));v=Mathf.Clamp(v,-1,1);
            if(r==0)longResponse=v;else if(r==1)shortAResponse=v;else shortBResponse=v;dirty=true;
        }
        public float GetTravel(int r)=>r==0?departureProgress:r==1?approachProgress:throw new ArgumentOutOfRangeException(nameof(r));
        public Vector3 SampleFlight(int index,float progress)
        {
            var p=flights.paths[index];float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress));
            return NodeCenter(flightAnchors[index])+(1-t)*(1-t)*p.startOffset+2*(1-t)*t*p.controlOffset+t*t*p.endOffset;
        }
        public void SetTravel(int r,float v)
        {
            if(r<0||r>1||!Finite(v))throw new ArgumentOutOfRangeException(nameof(r));
            if(r==0)departureProgress=Mathf.Clamp01(v);else approachProgress=Mathf.Clamp01(v);dirty=true;
        }
        public void Highlight(int region)
        {
            if(tint==null)return;
            for(int i=0;i<renderers.Length;i++)
            {
                var b=data.batches[i];if(b.kind!="wire")continue;
                int group=b.node>=0?RegionForNode(b.node):b.region;
                Color c=renderers[i].sharedMaterial.color;if(group==region)c=Color.Lerp(c,Color.white,.15f);
                tint.SetColor("_Color",c);renderers[i].SetPropertyBlock(tint);
            }
        }
        public void MarkDirty()=>dirty=true;
        private void OnDestroy(){if(meshes!=null)foreach(Mesh m in meshes)if(m!=null)Destroy(m);}
    }
}
