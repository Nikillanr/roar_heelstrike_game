using MTPAD.Rehab.Core;

namespace MTPAD.Rehab.Trigger
{
    public struct FootAngleSample
    {
        public FootSide side;
        public string trackerId;
        public long trackerSample;
        public double trackerTime;
        public float rawPitchDegrees;
        public bool valid;
    }
}
