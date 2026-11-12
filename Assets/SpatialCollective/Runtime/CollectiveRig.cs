using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialCollective
{
    /// <summary>Quadratic Bezier path through anchors for a detached cabin, including its final rotation angle.</summary>
    [Serializable] public sealed class DetachedPath
    {
        public string nodeId,anchorId;
        public Vector3 startOffset,controlOffset,endOffset;
        public float spinDegrees;
    }
    /// <summary>Path JSON version, shared playback duration, and path list.</summary>
    [Serializable] public sealed class FlightPathProfile
    {
        public int schemaVersion;
        public float duration;
        public DetachedPath[] paths;
    }
    /// <summary>Runtime model controller: calculate six-region deformation, coupling responses, and two detached-cabin paths, then update meshes.</summary>
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

        /// <summary>Validate references and paths, clone dynamic meshes, and initialize vertex-to-node visibility metadata.</summary>
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
                for(int v=0;v<nodeIds.Length;v++)nodeIds[v]=new Vector2(data.drivers[data.batches[i].vertexDrivers[v]].node,0);
                meshes[i].uv2=nodeIds; // Visibility metadata only; imported vertices/drivers remain unchanged.
                renderers[i].shadowCastingMode=ShadowCastingMode.Off;renderers[i].receiveShadows=false;
            }
            tint=new MaterialPropertyBlock();Ready=true;Rebuild();
        }
        /// <summary>Read and validate S08/O13 paths, anchors, and finite numeric values.</summary>
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
        /// <summary>Check that a value is neither NaN nor infinity.</summary>
        /// <param name="v">Scalar to check.</param>
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        /// <summary>Check that all three vector components are finite.</summary>
        /// <param name="v">Vector to check.</param>
        private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        /// <summary>Advance detached-cabin animation and rebuild changed meshes at the configured update frequency.</summary>
        private void Update()
        {
            // Call chain (Guide section 7):
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
        /// <summary>Calculate effective activation; in linked mode, add the core's offset from 0.5 multiplied by each branch response weight.</summary>
        private void EvaluateActivation()
        {
            Actual[0]=Mathf.Clamp01(core);float delta=Actual[0]-.5f;
            for(int i=1;i<4;i++)Actual[i]=Mathf.Clamp01(GetBaseline(i)+(coupled?GetCoupling(i-1)*delta:0));
            Actual[4]=Mathf.Clamp01(departureShape);Actual[5]=Mathf.Clamp01(approachShape);
        }
        /// <summary>Calculate driver poses and detached-cabin path transforms without uploading mesh vertices.</summary>
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
        /// <summary>Transform vertices for the current pose, update batch visibility and bounds, and notify listeners.</summary>
        public void Rebuild()
        {
            if(!Ready)return;
            // Call chain (Guide section 7): EvaluatePose -> batch vertex transforms -> Bounds -> GeometryChanged.
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
        /// <summary>Determine whether a node is adjacent to the selected node.</summary>
        /// <param name="n">Node index to check.</param>
        private bool IsNeighbor(int n)
        {if(n<0 || selectedNode<0 || selectedNode>=data.nodes.Length)return false;foreach(int other in data.nodes[selectedNode].neighbors)if(other==n)return true;return false;}
        /// <summary>Find a node by identifier, returning -1 when not found.</summary>
        /// <param name="id">Node identifier, such as S08.</param>
        public int FindNode(string id)=>Array.FindIndex(data.nodes,n=>n.id==id);
        /// <summary>Get the six-region parameter index for a node; detached cabins map to 4 and 5.</summary>
        /// <param name="node">Model node index.</param>
        public int RegionForNode(int node)=>node==flightNodes[0]?4:node==flightNodes[1]?5:data.nodes[node].region;
        /// <summary>Get a representative node for a region; regular regions use the first non-detached node.</summary>
        /// <param name="region">Region index, from 0 through 5.</param>
        public int NodeForRegion(int region)
        {
            if(region>=4)return flightNodes[region-4];
            for(int n=0;n<data.nodes.Length;n++)if(!data.nodes[n].detached && data.nodes[n].region==region)return n;
            return 0;
        }
        /// <summary>Get the deformed node center in model-local coordinates.</summary>
        /// <param name="n">Node index.</param>
        public Vector3 NodeCenter(int n)=>Matrices[n].MultiplyPoint3x4(data.nodes[n].center);
        /// <summary>Get the deformed node floor position in model-local coordinates.</summary>
        /// <param name="n">Node index.</param>
        public Vector3 NodeFloor(int n)=>Matrices[n].MultiplyPoint3x4(data.nodes[n].floor);
        /// <summary>Set a region's baseline deformation and mark the mesh for updating; clamp the value to 0-1.</summary>
        /// <param name="region">Region index: core, three branches, S08, and O13 map to 0-5.</param>
        /// <param name="value">Desired finite deformation value.</param>
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
        /// <summary>Read the region's baseline rather than its effective value after coupling.</summary>
        /// <param name="r">Region index from 0 to 5.</param>
        public float GetBaseline(int r)=>r==0?core:r==1?longBranch:r==2?shortA:r==3?shortB:r==4?departureShape:r==5?approachShape:throw new ArgumentOutOfRangeException(nameof(r));
        /// <summary>Read a branch's coupling response to the core.</summary>
        /// <param name="r">Branch index: long branch, short branch A, and short branch B map to 0-2.</param>
        public float GetCoupling(int r)=>r==0?longResponse:r==1?shortAResponse:r==2?shortBResponse:throw new ArgumentOutOfRangeException(nameof(r));
        /// <summary>Set the coupling response, clamped to -1 to 1; negative values reverse the response.</summary>
        /// <param name="r">Branch index from 0 to 2.</param>
        /// <param name="v">Finite response coefficient.</param>
        public void SetCoupling(int r,float v)
        {
            if(r<0||r>2||!Finite(v))throw new ArgumentOutOfRangeException(nameof(r));v=Mathf.Clamp(v,-1,1);
            if(r==0)longResponse=v;else if(r==1)shortAResponse=v;else shortBResponse=v;dirty=true;
        }
        /// <summary>Read a detached cabin's path progress.</summary>
        /// <param name="r">Path index: 0 for S08, 1 for O13.</param>
        public float GetTravel(int r)=>r==0?departureProgress:r==1?approachProgress:throw new ArgumentOutOfRangeException(nameof(r));
        /// <summary>Sample the smoothed quadratic Bezier path without changing current progress.</summary>
        /// <param name="index">Path index: 0 for S08, 1 for O13.</param>
        /// <param name="progress">Path progress, clamped to 0-1.</param>
        /// <returns>Sampled position in model-local coordinates.</returns>
        public Vector3 SampleFlight(int index,float progress)
        {
            var p=flights.paths[index];float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress));
            return NodeCenter(flightAnchors[index])+(1-t)*(1-t)*p.startOffset+2*(1-t)*t*p.controlOffset+t*t*p.endOffset;
        }
        /// <summary>Set detached-cabin path progress and mark geometry for updating.</summary>
        /// <param name="r">Path index: 0 for S08, 1 for O13.</param>
        /// <param name="v">Finite progress value, clamped to 0-1.</param>
        public void SetTravel(int r,float v)
        {
            if(r<0||r>1||!Finite(v))throw new ArgumentOutOfRangeException(nameof(r));
            if(r==0)departureProgress=Mathf.Clamp01(v);else approachProgress=Mathf.Clamp01(v);dirty=true;
        }
        /// <summary>Highlight the selected region's wireframe with a material property block without changing shared materials.</summary>
        /// <param name="region">Region index to highlight.</param>
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
        /// <summary>Request mesh recalculation on the next update cycle.</summary>
        public void MarkDirty()=>dirty=true;
        /// <summary>Destroy meshes cloned at runtime.</summary>
        private void OnDestroy(){if(meshes!=null)foreach(Mesh m in meshes)if(m!=null)Destroy(m);}
    }
}
