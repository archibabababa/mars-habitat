using System;
using System.IO;
using UnityEngine;

namespace SpatialCollective
{
    [Serializable] public sealed class StudyKey
    {
        public float time;
        public string phase;
        public float[] activation, travel;
    }
    [Serializable] public sealed class StudyProfile
    {
        public int schemaVersion;
        public float duration;
        public string referenceRoom;
        public StudyKey[] keys;
        public static StudyProfile Load(TextAsset source)
        {
            if(source==null)throw new InvalidDataException("Assign MotionStudy.json.");
            StudyProfile p=JsonUtility.FromJson<StudyProfile>(source.text);
            if(p==null || p.schemaVersion!=1 || !Finite(p.duration) || p.duration<=0 || p.keys==null || p.keys.Length<2)
                throw new InvalidDataException("Invalid motion study.");
            float previous=-1;
            foreach(var key in p.keys)
            {
                if(key==null || !Finite(key.time) || key.time<=previous || key.time>p.duration || string.IsNullOrWhiteSpace(key.phase))
                    throw new InvalidDataException("Study times must be finite and strictly increasing.");
                Check(key.activation,6);Check(key.travel,2);previous=key.time;
            }
            if(p.keys[0].time!=0 || p.keys[p.keys.Length-1].time!=p.duration || string.IsNullOrWhiteSpace(p.referenceRoom))
                throw new InvalidDataException("Study must cover its entire duration and name a reference room.");
            return p;
        }
        private static void Check(float[] values,int count)
        {
            if(values==null || values.Length!=count)throw new InvalidDataException("Study value count differs.");
            foreach(float v in values)if(!Finite(v)||v<0||v>1)throw new InvalidDataException("Study parameter outside 0..1.");
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }
    public sealed class CollectiveMotionStudy : MonoBehaviour
    {
        public CollectiveRig rig;
        public TextAsset sequenceJson;
        public StudyProfile Profile {get;private set;}
        public bool Playing {get;private set;}
        public float Position {get;private set;}
        public string Phase {get;private set;}="INTERACTIVE";
        public bool HasStarted {get;private set;}
        private void Awake(){Profile=StudyProfile.Load(sequenceJson);}
        private void Update()
        {
            if(!Playing)return;
            Seek(Mathf.Min(Profile.duration,Position+Time.unscaledDeltaTime));
            if(Position>=Profile.duration)Playing=false;
        }
        public void Toggle()
        {
            if(Playing){Playing=false;return;}
            if(!HasStarted || Position>=Profile.duration)Seek(0);
            HasStarted=true;Playing=true;
        }
        public void PauseForEdit(){Playing=false;HasStarted=false;Phase="INTERACTIVE";}
        public void Pause(){Playing=false;}
        public void Seek(float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            Position=Mathf.Clamp(seconds,0,Profile.duration); HasStarted=true;
            int i=0;while(i<Profile.keys.Length-2 && Position>=Profile.keys[i+1].time)i++;
            var a=Profile.keys[i];var b=Profile.keys[i+1];
            float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(a.time,b.time,Position));
            Phase=Position>=Profile.duration?b.phase:a.phase;
            rig.coupled=false;rig.animateDetached=false;
            for(int r=0;r<6;r++)rig.SetBaseline(r,Mathf.Lerp(a.activation[r],b.activation[r],t));
            for(int r=0;r<2;r++)rig.SetTravel(r,Mathf.Lerp(a.travel[r],b.travel[r],t));
        }
    }
}
