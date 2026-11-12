using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpatialCollective
{
    /// <summary>Explicitly labeled synthetic gesture rehearsal: demonstrate through the normal input path, then restore the original state.</summary>
    public sealed class CollectiveGestureRehearsal : MonoBehaviour
    {
        public CollectiveExperience experience;
        public bool Playing {get;private set;}
        public float Position {get;private set;}
        private sealed class Event {public float time;public Action run;}
        private readonly List<Event> events=new List<Event>();
        private int next;
        private bool automatic,userReleased;
        private float began,dragStart,dragEnd,from,to;
        private CollectiveParameterControl drag;
        private Transform lastTarget;
        private CollectiveSnapshot previousApplied,previousProposal;
        private bool previouslyComparing;
        /// <summary>Back up the user's state, initialize the rehearsal, and schedule approximately 60 seconds of demonstration events.</summary>
        /// <param name="playAutomatically">True advances in real time; otherwise the caller invokes Step.</param>
        public void Begin(bool playAutomatically)
        {
            if(experience.IsTransitioning)return;
            if(Playing)Stop();
            previouslyComparing=experience.stateController.IsComparing;
            experience.stateController.EndCompare();
            previousApplied=experience.stateController.CopyApplied();previousProposal=experience.stateController.Capture();
            automatic=playAutomatically;experience.ProcessHardwareInput=false;Playing=true;began=Time.unscaledTime;Position=0;next=0;events.Clear();drag=null;userReleased=false;
            experience.study.Pause();var rig=experience.rig;var state=experience.stateController;
            state.EndCompare();rig.animateDetached=false;rig.coupled=false;
            for(int i=0;i<6;i++)rig.SetBaseline(i,i==4?.2f:i==5?.8f:0);
            rig.SetTravel(0,0);rig.SetTravel(1,0);rig.Rebuild();state.Commit();
            experience.dashboard.RehearsalVisible=true;experience.dashboard.ShowTab(0);experience.dashboard.advanced.gameObject.SetActive(false);
            Add(0,()=>Chapter("01  SELECT / COLLECTIVE"));
            Add(1,()=>ClickDashboard(4));
            Add(4,()=>StartDrag(CollectiveParameter.Activation,0,1,4,11,"02  PINCH / UNFURL"));
            Add(11,()=>Release("03  RELEASE / PREVIEW"));
            Add(13,()=>{Click(CollectiveCommand.Apply);Chapter("04  APPLY / CONFIRMED");});
            Add(15,()=>ClickDashboard(5));
            Add(16,()=>StartDrag(CollectiveParameter.Activation,1,.15f,16,22,"05  PINCH / DIFFERENTIATE"));
            Add(22,()=>Release("REGIONAL PREVIEW / HELD"));
            Add(23,()=>ClickDashboard(1));
            Add(24,()=>StartDrag(CollectiveParameter.Activation,4,1,24,29,"06  S08 / CHANGE SHAPE"));
            Add(29,()=>Release("S08 SHAPE / PREVIEW"));
            Add(30,()=>StartDrag(CollectiveParameter.Travel,0,1,30,37,"07  S08 / DEPART"));
            Add(37,()=>Release("S08 DEPARTURE / HELD"));
            Add(38,()=>ClickDashboard(2));
            Add(39,()=>StartDrag(CollectiveParameter.Activation,5,.15f,39,44,"08  O13 / CHANGE SHAPE"));
            Add(44,()=>Release("O13 SHAPE / PREVIEW"));
            Add(45,()=>StartDrag(CollectiveParameter.Travel,1,1,45,51,"09  O13 / APPROACH"));
            Add(51,()=>Release("RELEASE / COMPLETE PROPOSAL"));
            Add(52,()=>{Click(CollectiveCommand.ToggleCompare);Chapter("10  COMPARE / APPLIED STATE");});
            Add(54,()=>{Click(CollectiveCommand.ToggleCompare);Chapter("11  RETURN TO PROPOSAL");});
            Add(56,()=>{Click(CollectiveCommand.Apply);Chapter("12  APPLY / CONFIRMED");});
            Add(58,()=>ClickDashboard(0));
            Step(0);
        }
        /// <summary>Add an event to the rehearsal timeline.</summary>
        /// <param name="t">Event trigger time in seconds.</param>
        /// <param name="action">Action to execute when triggered.</param>
        private void Add(float t,Action action)=>events.Add(new Event{time=t,run=action});
        /// <summary>Update the rehearsal chapter hint.</summary>
        /// <param name="label">Chapter text shown to the user.</param>
        private void Chapter(string label)=>experience.dashboard.DemonstrationChapter=label;
        /// <summary>Execute due events chronologically and update the simulated drag; time cannot move backward.</summary>
        /// <param name="time">Time in seconds since the rehearsal started.</param>
        public void Step(float time)
        {
            if(!Playing)return;
            if(time<Position)throw new InvalidOperationException("Input rehearsal must advance monotonically.");
            while(next<events.Count && events[next].time<=time)
            {
                UpdateDrag(events[next].time);events[next++].run();
            }
            UpdateDrag(time);Position=time;
            if(drag==null && lastTarget!=null)Send(lastTarget,false,1.2f);
            experience.rig.Rebuild();experience.SendMessage("LateUpdate");experience.SendMessage("RefreshStatus");
            experience.linkedViews.RenderNow();
        }
        /// <summary>Locate a parameter control and send a simulated pinch to begin a smooth drag.</summary>
        /// <param name="kind">Target parameter type.</param>
        /// <param name="index">Parameter index within that type.</param>
        /// <param name="value">Target value at the end of the drag.</param>
        /// <param name="start">Drag start time in seconds.</param>
        /// <param name="end">Drag end time in seconds.</param>
        /// <param name="chapter">Displayed rehearsal chapter.</param>
        private void StartDrag(CollectiveParameter kind,int index,float value,float start,float end,string chapter)
        {
            drag=experience.Menu.GetComponentsInChildren<CollectiveParameterControl>(true).First(c=>c.parameter==kind&&c.index==index);
            experience.dashboard.Reveal(drag);from=drag.Read(experience.rig);to=value;dragStart=start;dragEnd=end;
            lastTarget=drag.transform;Send(lastTarget,false,1.2f);Send(lastTarget,true,1.2f);Chapter(chapter);
            if(!experience.IsDragging)throw new InvalidOperationException("Rehearsal could not pinch "+drag.caption);
        }
        /// <summary>Interpolate simulated hand height over time and send held input.</summary>
        /// <param name="t">Current rehearsal time in seconds.</param>
        private void UpdateDrag(float t)
        {
            if(drag==null)return;
            float amount=Mathf.SmoothStep(0,1,Mathf.InverseLerp(dragStart,dragEnd,t));
            float height=1.2f+(to-from)*amount*experience.handTravel/(drag.Maximum-drag.Minimum);
            Send(lastTarget,true,height);
        }
        /// <summary>Release the current simulated drag and update the chapter.</summary>
        /// <param name="chapter">Chapter hint after release.</param>
        private void Release(string chapter)
        {
            if(drag!=null)Send(lastTarget,false,1.2f+(to-from)*experience.handTravel/(drag.Maximum-drag.Minimum));
            drag=null;Chapter(chapter);
        }
        /// <summary>Send synthetic hand input aimed at the target through normal interaction processing.</summary>
        /// <param name="target">Target transform for the simulated ray.</param>
        /// <param name="down">Whether the pinch is held.</param>
        /// <param name="height">Simulated hand height.</param>
        private void Send(Transform target,bool down,float height)
        {
            Physics.SyncTransforms();
            var ray=new Ray(experience.head.transform.position,(target.position-experience.head.transform.position).normalized);
            experience.ProcessInput(new CollectiveInputFrame{source=PointerSource.Hand,primary=down,height=height},ray);
        }
        /// <summary>Click the specified command button using a simulated pinch.</summary>
        /// <param name="command">Command to trigger.</param>
        private void Click(CollectiveCommand command)
        {
            var button=experience.Menu.GetComponentsInChildren<CollectiveButton>(true).First(b=>b.action==command);
            lastTarget=button.transform;Send(lastTarget,false,1.2f);Send(lastTarget,true,1.2f);Send(lastTarget,false,1.2f);
        }
        /// <summary>Click an internal panel action using a simulated pinch.</summary>
        /// <param name="action">Panel action ID.</param>
        private void ClickDashboard(int action)
        {
            lastTarget=experience.Menu.GetComponentsInChildren<CollectiveDashboardAction>(true).First(a=>a.action==action).transform;
            Send(lastTarget,false,1.2f);Send(lastTarget,true,1.2f);Send(lastTarget,false,1.2f);
        }
        /// <summary>End the rehearsal, restore the applied state and preview, and re-enable hardware input.</summary>
        public void Stop()
        {
            if(drag!=null)Release("PREVIEW HELD");Playing=false;experience.ProcessHardwareInput=true;
            experience.stateController.EndCompare();
            if(previousApplied!=null)
            {
                experience.stateController.Restore(previousApplied);experience.stateController.Commit();
                experience.stateController.Restore(previousProposal);
                if(previouslyComparing)experience.stateController.BeginCompare();
            }
            experience.dashboard.HideRehearsal();experience.dashboard.DemonstrationChapter="";
            experience.SuspendInteraction();
        }
        /// <summary>Advance the rehearsal automatically; abort on a new user pinch, undo, or Escape.</summary>
        private void Update()
        {
            if(!Playing||!automatic)return;
            var user=experience.inputReader.Read();if(!user.primary)userReleased=true;
            if((userReleased&&user.primary)||user.undo||(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)){Stop();return;}
            float t=Time.unscaledTime-began;Step(Mathf.Min(60,t));if(t>=60)Stop();
        }
    }
}
