using UnityEngine;

namespace SpatialCollective
{
    public sealed class CollectiveBimanualSession
    {
        public bool Active {get;private set;}
        public bool Began {get;private set;}
        public bool Ended {get;private set;}
        public bool NeedsOpen {get;private set;}=true;
        public float Delta {get;private set;}
        private float startSpan;
        public void Step(CollectiveInputFrame frame,float travel)
        {
            Began=Ended=false;
            bool valid=frame.leftTracked&&frame.rightTracked&&
                (frame.source==PointerSource.Hand||frame.source==PointerSource.HandGaze);
            float span=Vector3.Distance(frame.leftPosition,frame.rightPosition);
            valid&=!float.IsNaN(span)&&!float.IsInfinity(span)&&span>.06f&&span<1.6f;
            if(!valid)
            {
                Ended=Active;Active=false;NeedsOpen=true;return;
            }
            if(NeedsOpen)
            {
                if(!frame.primary&&!frame.secondary)NeedsOpen=false;
                return;
            }
            if(Active&&(!frame.primary||!frame.secondary))
            {Active=false;Ended=true;NeedsOpen=true;return;}
            if(!Active&&frame.primary&&frame.secondary)
            {startSpan=span;Delta=0;Active=Began=true;}
            if(Active)Delta=Mathf.Abs(span-startSpan)<.006f?0:(span-startSpan)/Mathf.Max(.1f,travel);
        }
        public void Suspend(){Active=false;NeedsOpen=true;Delta=0;}
    }
}
