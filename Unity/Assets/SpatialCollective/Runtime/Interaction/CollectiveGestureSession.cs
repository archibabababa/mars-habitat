using UnityEngine;

namespace SpatialCollective
{
    public sealed class CollectiveGestureSession
    {
        public bool Began { get; private set; }
        public bool Released { get; private set; }
        public bool Interrupted { get; private set; }
        public bool SourceChanged { get; private set; }
        public bool Held { get; private set; }
        public bool NeedsRelease { get; private set; } = true;
        private PointerSource source;
        private bool wasDown;
        public void Step(CollectiveInputFrame frame)
        {
            Began = Released = Interrupted = SourceChanged = false;
            bool valid = frame.source != PointerSource.None && !float.IsNaN(frame.height) && !float.IsInfinity(frame.height);
            if (!valid || source != frame.source)
            {
                Interrupted = Held;
                SourceChanged = true;
                Held = wasDown = false; NeedsRelease = true; source = valid ? frame.source : PointerSource.None;
            }
            if (!valid) return;
            if (NeedsRelease)
            {
                if (!frame.primary) NeedsRelease = false;
                return;
            }
            Began = frame.primary && !wasDown;
            Released = !frame.primary && wasDown;
            Held = frame.primary; wasDown = frame.primary;
        }
        public void Suspend()
        {
            source = PointerSource.None; Held = wasDown = false; NeedsRelease = true;
        }
    }
}
