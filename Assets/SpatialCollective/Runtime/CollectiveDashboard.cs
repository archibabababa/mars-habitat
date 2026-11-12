using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpatialCollective
{
    /// <summary>World-space engineering dashboard showing live overview, cabin, status and parameters while leaving the 3D scene visible on the left.</summary>
    public sealed partial class CollectiveDashboard : MonoBehaviour
    {
        public const float Unit=.0018f;
        public CollectiveExperience experience;
        public TextMesh status,help,phase,gestureText,bodyText,roomTitle,travelText;
        public Transform collectiveControls,departureControls,approachControls,advanced;
        public Renderer overviewScreen,roomScreen;
        public CollectiveViewportTarget overviewTarget;
        public int ActiveTab {get;private set;}
        public string DemonstrationChapter="";
        public bool RehearsalVisible;
        public bool HeadsetLayout {get;private set;}
        private readonly List<Material> owned=new List<Material>();
        [SerializeField] private LineRenderer[] icon;
        private LineRenderer[] largeHand;
        private LineRenderer rehearsalRay;
        private TextMesh rehearsalLabel;
        [SerializeField] private TextMesh linkedRoomTitle;
        [SerializeField] private TextMesh departureTag,approachTag;
        [SerializeField] private Transform iconRoot;
        private Transform handRoot,cursor;
        private bool pinched;
        private float gestureHeight,initialHeight;
        private Vector3 physicalReference;
        private Color Ink=>new Color(.87f,.92f,.95f);
        private Color Cyan=>new Color(.30f,.85f,.88f);
        /// <summary>Convert 1920x1080 design coordinates to panel-local coordinates.</summary>
        /// <param name="x">Horizontal pixel coordinate measured from the left.</param>
        /// <param name="y">Vertical pixel coordinate measured from the top.</param>
        /// <param name="z">Panel-local depth.</param>
        public static Vector3 Pixel(float x,float y,float z=0)=>new Vector3((x-960)*Unit,(540-y)*Unit,z);

        /// <summary>Build the dashboard, parameter bars, buttons and view containers from configuration.</summary>
        /// <param name="profile">Interaction profile supplying menu bindings.</param>
        public void Build(InteractionProfile profile)
        {
            // Opaque margins frame an unobstructed, true 3D left pane (32,132)-(1304,848).
            Panel("Header",transform,960,66,1920,132);
            Panel("Footer",transform,960,964,1920,232);
            Panel("Right column",transform,1612,490,616,716);
            Panel("Left margin",transform,16,490,32,716);
            Label("Brand","SPATIAL COLLECTIVE",32,34,18,Ink);
            Label("Title","A living habitat, experienced from within.",32,82,36,Ink);
            phase=Label("Phase","POINT / PINCH / DRAG",1888,50,23,Cyan,TextAnchor.MiddleRight);
            status=Label("State","PREVIEW / NOT APPLIED",1888,90,17,Ink,TextAnchor.MiddleRight);
            Panel("Local label background",transform,260,165,440,38);
            roomTitle=Label("Local room","LOCAL PANORAMA / REMOTE S05",60,165,17,Cyan);
            Label("Collective caption","COLLECTIVE / SAME CONFIGURATION",1336,138,18,Cyan);
            overviewScreen=Screen("Collective live viewport",1336,159,552,307,true);
            overviewTarget=overviewScreen.GetComponent<CollectiveViewportTarget>();
            linkedRoomTitle=Label("Room caption","REMOTE WINDOW / LINKED SPACES",1336,500,18,Cyan);
            roomScreen=Screen("Remote room live viewport",1336,521,552,318,false);
            travelText=Label("Travel summary","S08 DEPARTS     O13 APPROACHES",1350,449,14,Ink);
            departureTag=Label("S08 marker","S08",1500,230,15,new Color(1,.75f,.38f),TextAnchor.MiddleCenter);
            approachTag=Label("O13 marker","O13",1700,230,15,new Color(.72f,.68f,1),TextAnchor.MiddleCenter);

            Panel("Body card",transform,170,951,276,150);
            Label("Body title","LOCAL BODY",48,896,17,Ink);
            bodyText=Label("Body displacement","0.00 m",292,896,17,Ink,TextAnchor.MiddleRight);
            Label("Body meaning","Observation only",170,1005,15,Cyan,TextAnchor.MiddleCenter);
            DrawBodyIcon();

            Panel("Control card",transform,818,950,974,152);
            help=Label("Instruction","Select a region; pinch and drag upward.",352,900,20,Ink);
            collectiveControls=Group("Collective controls");departureControls=Group("S08 controls");approachControls=Group("O13 controls");
            string[] names={"CORE","LONG","SHORT A","SHORT B"};
            Color[] colors={Cyan,new Color(.48f,.86f,.66f),new Color(.71f,.60f,.90f),new Color(.54f,.68f,.96f)};
            for(int i=0;i<4;i++)Parameter(collectiveControls,CollectiveParameter.Activation,i,names[i],448+i*235,951,194,colors[i]);
            Parameter(departureControls,CollectiveParameter.Activation,4,"S08 SHAPE",490,951,270,Cyan);
            Parameter(departureControls,CollectiveParameter.Travel,0,"S08 DEPART",850,951,270,new Color(1,.72f,.35f));
            Label("Departure note","Shape and travel\nare independent",1150,950,17,Ink,TextAnchor.MiddleCenter,departureControls);
            Parameter(approachControls,CollectiveParameter.Activation,5,"O13 SHAPE",490,951,270,Cyan);
            Parameter(approachControls,CollectiveParameter.Travel,1,"O13 APPROACH",850,951,270,new Color(.7f,.65f,1));
            Label("Approach note","Shape and travel\nare independent",1150,950,17,Ink,TextAnchor.MiddleCenter,approachControls);
            Label("Gesture sequence","PINCH TO SELECT  >  DRAG TO ADJUST  >  RELEASE TO PREVIEW",352,999,13,Ink);
            // Primary actions remain available without opening the engineering drawer.
            var primary=new HashSet<CollectiveCommand>{CollectiveCommand.Apply,CollectiveCommand.Undo,CollectiveCommand.ToggleCompare};
            advanced=Group("More controls");Panel("More backdrop",advanced,818,694,974,260);
            Label("More heading","MORE CONTROLS / MODEL + SESSION",352,590,17,Cyan,TextAnchor.MiddleLeft,advanced);
            int extra=0;
            for(int i=0;i<profile.menu.Length;i++)
            {
                var binding=profile.menu[i];float x,y,w;
                Transform parent;
                if(primary.Contains(binding.ParsedCommand))
                {
                    int slot=binding.ParsedCommand==CollectiveCommand.Apply?2:binding.ParsedCommand==CollectiveCommand.Undo?0:1;
                    x=1025+slot*108;y=1000;w=100;parent=transform;
                }
                else {x=436+(extra%4)*244;y=635+(extra/4)*34;w=228;parent=advanced;extra++;}
                var b=Button(binding.label,parent,x,y,w,29);
                var action=b.AddComponent<CollectiveButton>();action.bindingIndex=i;action.action=binding.ParsedCommand;action.argument=binding.argument;
                action.label=Label("Label",binding.label,x,y,14,Ink,TextAnchor.MiddleCenter,parent);
            }
            for(int i=0;i<3;i++)Parameter(advanced,CollectiveParameter.Coupling,i,new[]{"LONG LINK","SHORT A LINK","SHORT B LINK"}[i],455+i*330,806,280,Cyan);
            DashboardButton("COLLECTIVE",0,440,828,192);
            DashboardButton("S08 / DEPART",1,654,828,192);
            DashboardButton("O13 / APPROACH",2,868,828,212);
            DashboardButton("LINK ALL",4,1090,828,120);
            DashboardButton("FREE",5,1192,828,74);
            DashboardButton("MORE",3,1271,828,64);
            DashboardButton("GESTURE DEMO",6,1180,166,210);

            Panel("Gesture card",transform,1612,928,552,122);
            gestureText=Label("Gesture state","POINT / SELECT",1450,901,23,Cyan);
            Label("Input meaning","Head + body = observation\nSelected input = deformation",1450,948,17,Ink);
            iconRoot=Group("Input gesture indicator");iconRoot.localPosition=Pixel(1385,945,-.025f);
            icon=HandLines(iconRoot,.0012f);
            Label("Credits","Concept: Fan Huang | AI-assisted assets and code",1888,1022,12,Ink,TextAnchor.MiddleRight);
            Label("Preview note","Unity runtime / panorama reference: NASA Cupola",1888,1045,12,Ink,TextAnchor.MiddleRight);
            Label("Bottom note","Spherical background + translucent 3D space / live linked views",32,1056,13,Ink);
        }
        /// <summary>Initialize the default tab, body reference position and rehearsal-only simulated hands.</summary>
        public void Initialize()
        {
            physicalReference=experience.head.transform.position;
            ShowTab(0);advanced.gameObject.SetActive(false);
            // The large outline is enabled only for explicitly labelled input rehearsals.
            handRoot=Group("Synthetic rehearsal hand outline");largeHand=HandLines(handRoot,.0035f);
            var handInk=new Material(experience.handMaterial){color=Color.white,renderQueue=3530};owned.Add(handInk);
            foreach(var finger in largeHand){finger.sharedMaterial=handInk;finger.widthMultiplier=.006f;}
            handRoot.gameObject.SetActive(false);
            var line=new GameObject("Illustrative hand-to-target guide");line.layer=9;line.transform.SetParent(transform,false);
            rehearsalRay=line.AddComponent<LineRenderer>();rehearsalRay.sharedMaterial=experience.handMaterial;rehearsalRay.useWorldSpace=true;rehearsalRay.widthMultiplier=.0015f;rehearsalRay.positionCount=2;rehearsalRay.enabled=false;
            rehearsalLabel=Label("Rehearsal disclosure","SIMULATED GESTURE INPUT / Unity runtime",60,790,17,Cyan);
            rehearsalLabel.gameObject.SetActive(false);
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(go.GetComponent<Collider>());
            go.name="Gesture target dot";go.layer=9;go.transform.SetParent(transform,false);go.transform.localScale=Vector3.one*.013f;
            go.GetComponent<Renderer>().sharedMaterial=experience.handMaterial;cursor=go.transform;cursor.gameObject.SetActive(false);
        }
        /// <summary>Bind offscreen overview and cabin textures to the dashboard.</summary>
        /// <param name="views">Linked views supplying render textures and source cameras.</param>
        public void BindViews(CollectiveLinkedViews views)
        {
            overviewScreen.sharedMaterial=TextureMaterial(views.OverviewTexture);
            roomScreen.sharedMaterial=TextureMaterial(views.RoomTexture);
            overviewTarget.sourceCamera=views.OverviewCamera;
        }
        /// <summary>Switch the engineering layout between whole, S08 and O13 parameter groups.</summary>
        /// <param name="tab">Tab index: 0=whole, 1=S08, 2=O13.</param>
        public void ShowTab(int tab)
        {
            if(HeadsetLayout)return;
            ActiveTab=tab;collectiveControls.gameObject.SetActive(tab==0);departureControls.gameObject.SetActive(tab==1);approachControls.gameObject.SetActive(tab==2);
            experience.InspectRegion(tab==0?0:tab+3);
        }
        /// <summary>Show the tab or advanced panel containing a parameter for rehearsal targeting.</summary>
        /// <param name="control">Parameter control to show.</param>
        public void Reveal(CollectiveParameterControl control)
        {
            if(control.parameter==CollectiveParameter.Coupling)advanced.gameObject.SetActive(true);
            else {advanced.gameObject.SetActive(false);ShowTab(control.parameter==CollectiveParameter.Travel?control.index+1:control.index<4?0:control.index-3);}
            Physics.SyncTransforms();
        }
        /// <summary>Handle internal tab, more, linked/free mode and rehearsal actions.</summary>
        /// <param name="action">Action ID from CollectiveDashboardAction.</param>
        public void HandleAction(int action)
        {
            if(action<3){ShowTab(action);experience.Execute(CollectiveCommand.SelectRegion,action==0?0:action+3);advanced.gameObject.SetActive(false);return;}
            if(action==3){advanced.gameObject.SetActive(!advanced.gameObject.activeSelf);return;}
            if(action==6){if(!HeadsetLayout)experience.rehearsal.Begin(true);return;}
            experience.study.PauseForEdit();experience.rig.animateDetached=false;
            if(action==4)
            {
                experience.Execute(CollectiveCommand.SelectRegion,0);
                for(int i=1;i<4;i++){experience.rig.SetBaseline(i,.5f);experience.rig.SetCoupling(i-1,1);}
                experience.rig.coupled=true;ShowTab(0);
            }
            else
            {
                experience.rig.Rebuild();float[] actual=(float[])experience.rig.Actual.Clone();
                experience.rig.coupled=false;for(int i=0;i<4;i++)experience.rig.SetBaseline(i,actual[i]);
            }
            experience.rig.MarkDirty();
        }
        /// <summary>Update input hints and rehearsal hand illustrations; the headset read-only layout hides these engineering visuals.</summary>
        /// <param name="frame">Current input frame.</param>
        /// <param name="target">World-space pointer or simulated rehearsal target.</param>
        public void ObserveInput(CollectiveInputFrame frame,Vector3 target)
        {
            if(HeadsetLayout)return;
            if(frame.primary&&!pinched)initialHeight=frame.height;
            pinched=frame.primary;gestureHeight=frame.height-initialHeight;
            DrawHand(icon,pinched);
            if(handRoot!=null)
            {
                handRoot.gameObject.SetActive(RehearsalVisible&&!HeadsetLayout);cursor.gameObject.SetActive(RehearsalVisible&&!HeadsetLayout);
                handRoot.localPosition=Pixel(1130,705-Mathf.Clamp(gestureHeight,-.4f,.4f)*340,-.05f);
                DrawHand(largeHand,pinched);
                cursor.position=target-transform.forward*.02f;
                rehearsalRay.enabled=RehearsalVisible&&pinched&&!HeadsetLayout;rehearsalRay.SetPosition(0,handRoot.position);rehearsalRay.SetPosition(1,cursor.position);
                rehearsalLabel.gameObject.SetActive(RehearsalVisible&&!HeadsetLayout);
            }
            gestureText.text=experience.IsBimanual?"BOTH HANDS / SPAN":experience.IsDragging?"PINCH + DRAG":frame.primary?"PINCH":experience.stateController.HasChanges?"RELEASE / PREVIEW":"POINT / SELECT";
        }
        /// <summary>Refresh text from the model, applied state, body displacement and linked views.</summary>
        public void Refresh()
        {
            if(HeadsetLayout){RefreshReadout();return;}
            var rig=experience.rig;var state=experience.stateController;
            status.text=state.IsComparing?"APPLIED / COMPARING":state.HasChanges?"PREVIEW / NOT APPLIED":"APPLIED";
            phase.text=string.IsNullOrEmpty(DemonstrationChapter)?(rig.coupled?"COLLECTIVE / LINKED":"REGIONAL / FREE"):DemonstrationChapter;
            help.text=experience.IsDragging?"Pinch held: move up / down to change the selected value.":state.HasChanges?"Release holds the proposal. Apply when ready.":"Select the collective or a detached unit; pinch and drag.";
            roomTitle.text="LOCAL PANORAMA / REMOTE "+rig.data.nodes[rig.selectedNode].id;
            travelText.text=$"S08 DEPARTS  {rig.departureProgress:0.00}       O13 APPROACHES  {rig.approachProgress:0.00}";
            float displacement=(experience.head.transform.position-physicalReference).magnitude;
            bodyText.text=$"{displacement:0.00} m";
            if(!string.IsNullOrEmpty(experience.linkedViews.RoomId))linkedRoomTitle.text="REMOTE "+experience.linkedViews.RoomId+" / LINKED SPACES";
            if(experience.linkedViews.OverviewCamera!=null)
            {
                PositionTag(departureTag,"S08");PositionTag(approachTag,"O13");
            }
            if(HeadsetLayout)
            {
                help.text=WrapFeedback(experience.Feedback,78);
                gestureText.text="TARGET: "+experience.SelectedParameter+(experience.IsBimanual?" / TWO HANDS":"");
                bodyText.text=experience.roomScale!=null?experience.roomScale.Status:"ROOM SCALE / 1:1";
                roomTitle.text="SPATIAL COLLECTIVE / "+rig.data.nodes[rig.selectedNode].id;
            }
        }
        /// <summary>Hide rehearsal hands, targets, guide lines and simulated-input labels.</summary>
        public void HideRehearsal()
        {
            RehearsalVisible=false;if(handRoot!=null)handRoot.gameObject.SetActive(false);
            if(cursor!=null)cursor.gameObject.SetActive(false);if(rehearsalRay!=null)rehearsalRay.enabled=false;
            if(rehearsalLabel!=null)rehearsalLabel.gameObject.SetActive(false);
        }
        /// <summary>Move a node label using its overview-camera viewport position.</summary>
        /// <param name="tag">Text component to move.</param>
        /// <param name="id">Target node identifier.</param>
        private void PositionTag(TextMesh tag,string id)
        {
            Vector3 point=experience.linkedViews.OverviewCamera.WorldToViewportPoint(experience.rig.transform.TransformPoint(experience.rig.NodeCenter(experience.rig.FindNode(id))));
            if(HeadsetLayout){tag.transform.localPosition=new Vector3(1.15f+(Mathf.Clamp01(point.x)-.5f)*.68f,.21f+(Mathf.Clamp01(point.y)-.5f)*.38f,-.023f);return;}
            tag.transform.localPosition=Pixel(Mathf.Clamp(1336+point.x*552,1360,1866),Mathf.Clamp(159+(1-point.y)*307-17,179,433),-.023f);
        }
        /// <summary>Create a UI group under the dashboard.</summary>
        /// <param name="name">Group object name.</param>
        private Transform Group(string name){var g=new GameObject(name);g.layer=9;g.transform.SetParent(transform,false);return g.transform;}
        /// <summary>Create a textured panel material and register it for disposal.</summary>
        /// <param name="texture">Texture to display.</param>
        private Material TextureMaterial(Texture texture)
        {var m=new Material(experience.insetMaterial){mainTexture=texture,renderQueue=3501};owned.Add(m);return m;}
        /// <summary>Create an engineering view screen with a collider.</summary>
        /// <param name="name">Object name.</param>
        /// <param name="x">Top-left horizontal coordinate.</param>
        /// <param name="y">Top-left vertical coordinate.</param>
        /// <param name="w">Width in design pixels.</param>
        /// <param name="h">Height in design pixels.</param>
        /// <param name="pick">Whether to add the overview picking component.</param>
        private Renderer Screen(string name,float x,float y,float w,float h,bool pick)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Quad);g.name=name;g.layer=9;DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(transform,false);g.transform.localPosition=Pixel(x+w*.5f,y+h*.5f,-.006f);g.transform.localScale=new Vector3(w*Unit,h*Unit,1);
            var c=g.AddComponent<BoxCollider>();c.size=new Vector3(1,1,.006f);
            if(pick)g.AddComponent<CollectiveViewportTarget>();
            var r=g.GetComponent<Renderer>();r.sharedMaterial=experience.uiMaterial;return r;
        }
        /// <summary>Create a panel background and collision plane.</summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Parent transform.</param>
        /// <param name="x">Horizontal centre coordinate.</param>
        /// <param name="y">Vertical centre coordinate.</param>
        /// <param name="w">Width in design pixels.</param>
        /// <param name="h">Height in design pixels.</param>
        private void Panel(string name,Transform parent,float x,float y,float w,float h)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Quad);g.name=name;g.layer=9;DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent,false);g.transform.localPosition=Pixel(x,y,-.004f);g.transform.localScale=new Vector3(w*Unit,h*Unit,1);
            g.GetComponent<Renderer>().sharedMaterial=experience.uiMaterial;
            g.AddComponent<BoxCollider>().size=new Vector3(1,1,.002f);
        }
        /// <summary>Split feedback into at most two lines, truncating an overlong second line with an ellipsis.</summary>
        /// <param name="value">Original feedback text.</param>
        /// <param name="width">Maximum characters per line.</param>
        private static string WrapFeedback(string value,int width)
        {
            if(value.Length<=width)return value;
            int split=value.LastIndexOf(' ',width);if(split<1)split=width;
            string rest=value.Substring(split).TrimStart();
            return value.Substring(0,split)+"\n"+(rest.Length>width?rest.Substring(0,width-3)+"...":rest);
        }
        /// <summary>Create panel text.</summary>
        /// <param name="name">Object name.</param>
        /// <param name="text">Initial text.</param>
        /// <param name="x">Design horizontal coordinate.</param>
        /// <param name="y">Design vertical coordinate.</param>
        /// <param name="size">Design font size.</param>
        /// <param name="color">Text colour.</param>
        /// <param name="anchor">Text anchor and alignment.</param>
        /// <param name="parent">Parent transform, defaulting to the panel root when null.</param>
        private TextMesh Label(string name,string text,float x,float y,float size,Color color,TextAnchor anchor=TextAnchor.MiddleLeft,Transform parent=null)
        {
            var g=new GameObject(name);g.layer=9;g.transform.SetParent(parent==null?transform:parent,false);g.transform.localPosition=Pixel(x,y,-.018f);
            var t=g.AddComponent<TextMesh>();t.font=experience.font;t.fontSize=64;t.characterSize=size*Unit*10/64;
            t.anchor=anchor;t.alignment=anchor==TextAnchor.MiddleCenter?TextAlignment.Center:anchor==TextAnchor.MiddleRight?TextAlignment.Right:TextAlignment.Left;
            t.text=text;t.color=color;g.GetComponent<Renderer>().sharedMaterial=experience.font.material;return t;
        }
        /// <summary>Create a ray-hittable button container.</summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Parent transform.</param>
        /// <param name="x">Horizontal centre coordinate.</param>
        /// <param name="y">Vertical centre coordinate.</param>
        /// <param name="width">Width in design pixels.</param>
        /// <param name="height">Height in design pixels.</param>
        private GameObject Button(string name,Transform parent,float x,float y,float width,float height)
        {
            var g=new GameObject(name);g.layer=9;g.transform.SetParent(parent,false);g.transform.localPosition=Pixel(x,y,-.022f);
            var c=g.AddComponent<BoxCollider>();c.size=new Vector3(width*Unit,height*Unit,.018f);
            return g;
        }
        /// <summary>Create a panel button with background, action marker and label.</summary>
        /// <param name="name">Button name and label.</param>
        /// <param name="action">Panel action ID.</param>
        /// <param name="x">Horizontal centre coordinate.</param>
        /// <param name="y">Vertical centre coordinate.</param>
        /// <param name="width">Button width in design pixels.</param>
        private void DashboardButton(string name,int action,float x,float y,float width)
        {Panel(name+" surface",transform,x,y,width,30);Button(name,transform,x,y,width,30).AddComponent<CollectiveDashboardAction>().action=action;Label(name+" label",name,x,y,13,Cyan,TextAnchor.MiddleCenter);}
        /// <summary>Create a parameter control, value label and fill bar.</summary>
        /// <param name="parent">Control parent transform.</param>
        /// <param name="kind">Parameter type.</param>
        /// <param name="index">Parameter index within its type.</param>
        /// <param name="caption">Parameter caption.</param>
        /// <param name="x">Horizontal centre coordinate.</param>
        /// <param name="y">Vertical centre coordinate.</param>
        /// <param name="width">Control width in design pixels.</param>
        /// <param name="color">Text colour.</param>
        private void Parameter(Transform parent,CollectiveParameter kind,int index,string caption,float x,float y,float width,Color color)
        {
            var g=Button(caption,parent,x,y,width,63);var c=g.AddComponent<CollectiveParameterControl>();c.parameter=kind;c.index=index;c.caption=caption;c.width=width*Unit;
            c.displayActual=true;c.label=Label(caption+" value",caption,x-width*.5f,y-10,16,color,TextAnchor.MiddleLeft,parent);
            c.labelColor=color;c.fill=GameObject.CreatePrimitive(PrimitiveType.Quad).transform;c.fill.name=caption+" fill";c.fill.gameObject.layer=9;
            DestroyImmediate(c.fill.GetComponent<Collider>());c.fill.SetParent(g.transform,false);c.fill.GetComponent<Renderer>().sharedMaterial=experience.handMaterial;
        }
        /// <summary>Create schematic lines for five fingers.</summary>
        /// <param name="parent">Parent transform of the hand illustration.</param>
        /// <param name="scale">Overall illustration scale and line-width reference.</param>
        private LineRenderer[] HandLines(Transform parent,float scale)
        {
            parent.localScale=Vector3.one*scale;var result=new LineRenderer[5];
            for(int i=0;i<5;i++){var g=new GameObject("Finger "+i);g.layer=9;g.transform.SetParent(parent,false);var l=g.AddComponent<LineRenderer>();l.sharedMaterial=experience.handMaterial;l.useWorldSpace=false;l.widthMultiplier=scale*1.1f;l.positionCount=5;result[i]=l;}
            DrawHand(result,false);return result;
        }
        /// <summary>Update finger polylines in the hand illustration.</summary>
        /// <param name="lines">Line renderers for five fingers.</param>
        /// <param name="closed">Whether to show a pinching pose.</param>
        private static void DrawHand(LineRenderer[] lines,bool closed)
        {
            if(lines==null)return;
            for(int f=0;f<5;f++)
            {
                float x=(f-1.5f)*8;
                Vector3[] p={new Vector3(-8,-22,0),new Vector3(x,-3,0),new Vector3(x,13,0),new Vector3(x,26,0),new Vector3(x,36-f*2,0)};
                if(f==0){p[1]=new Vector3(-19,-1,0);p[2]=new Vector3(-20,9,0);p[3]=new Vector3(-11,16,0);p[4]=closed?new Vector3(-1,21,0):new Vector3(-12,28,0);}
                else if(f==1&&closed){p[3]=new Vector3(3,29,0);p[4]=new Vector3(-1,21,0);}
                else if(f>1){p[3]=new Vector3(x+3,12,0);p[4]=new Vector3(x-2,7,0);}
                lines[f].SetPositions(p);
            }
        }
        /// <summary>Draw the local body-observation diagram.</summary>
        private void DrawBodyIcon()
        {
            var root=Group("Local body diagram");root.localPosition=Pixel(170,957,-.02f);
            var line=root.gameObject.AddComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=experience.handMaterial;line.widthMultiplier=.002f;line.positionCount=40;line.loop=true;
            for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40;line.SetPosition(i,new Vector3(Mathf.Cos(a)*.12f,Mathf.Sin(a)*.035f,0));}
        }
        /// <summary>Destroy dashboard-owned materials.</summary>
        private void OnDestroy(){foreach(var m in owned)if(m!=null)Destroy(m);}
    }
}
