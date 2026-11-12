using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Map changes in hand separation to parameter deltas without modifying geometry directly; both hands must open after tracking loss.</summary>
    public sealed class CollectiveBimanualSession
    {
        public bool Active {get;private set;}
        public bool Began {get;private set;}
        public bool Ended {get;private set;}
        public bool NeedsOpen {get;private set;}=true;
        public float Delta {get;private set;}
        private float startSpan;
        /// <summary>Update two-hand gesture start, hold, and end states and the normalized separation delta.</summary>
        /// <param name="frame">Input frame containing tracking, pinch states, and world positions for both hands.</param>
        /// <param name="travel">Hand travel corresponding to a unit parameter change, using a minimum of 0.1 m.</param>
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
        /// <summary>Abort the current gesture and require both hands to open again.</summary>
        public void Suspend(){Active=false;NeedsOpen=true;Delta=0;}
    }
}
