using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class FootAngleEstimator : MonoBehaviour
    {
        public UnityXrTrackerSource trackerSource;
        public GroundFrameCalibration groundFrame;

        [Header("Flat-foot calibration")]
        public bool requireGroundFrame = true;
        public bool invertLeft;
        public bool invertRight;
        [SerializeField] private bool leftCalibrated;
        [SerializeField] private bool rightCalibrated;
        [SerializeField] private Quaternion leftSensorToFlat = Quaternion.identity;
        [SerializeField] private Quaternion rightSensorToFlat = Quaternion.identity;

        [Header("Live angles")]
        [SerializeField] private float leftPitchDegrees;
        [SerializeField] private float rightPitchDegrees;
        [SerializeField] private bool leftValid;
        [SerializeField] private bool rightValid;
        [SerializeField] private string status = "Not calibrated";

        private FootAngleSample leftSample;
        private FootAngleSample rightSample;

        public float LeftPitchDegrees { get { return leftPitchDegrees; } }
        public float RightPitchDegrees { get { return rightPitchDegrees; } }
        public bool LeftValid { get { return leftValid; } }
        public bool RightValid { get { return rightValid; } }
        public bool LeftCalibrated { get { return leftCalibrated; } }
        public bool RightCalibrated { get { return rightCalibrated; } }
        public string Status { get { return status; } }

        private void Update()
        {
            leftSample = Calculate(FootSide.Left, leftSensorToFlat, leftCalibrated, invertLeft);
            rightSample = Calculate(FootSide.Right, rightSensorToFlat, rightCalibrated, invertRight);
            leftPitchDegrees = leftSample.rawPitchDegrees;
            rightPitchDegrees = rightSample.rawPitchDegrees;
            leftValid = leftSample.valid;
            rightValid = rightSample.valid;
            status = leftValid && rightValid ? "Angles valid" : "Waiting for valid calibrated trackers";
        }

        public bool TryGetLatest(FootSide side, out FootAngleSample sample)
        {
            sample = side == FootSide.Left ? leftSample : rightSample;
            return sample.valid;
        }

        [ContextMenu("Capture Flat-Foot Calibration")]
        public void CaptureFlatFootCalibration()
        {
            TrackerPoseSample raw;
            if (TryCanonicalPose(FootSide.Left, out raw))
            {
                leftSensorToFlat = Quaternion.Inverse(raw.rotation);
                leftCalibrated = true;
            }
            if (TryCanonicalPose(FootSide.Right, out raw))
            {
                rightSensorToFlat = Quaternion.Inverse(raw.rotation);
                rightCalibrated = true;
            }
            status = leftCalibrated && rightCalibrated
                ? "Flat-foot calibration captured"
                : "Flat-foot capture requires both valid trackers";
        }

        [ContextMenu("Clear Flat-Foot Calibration")]
        public void ClearFlatFootCalibration()
        {
            leftCalibrated = false;
            rightCalibrated = false;
            leftSensorToFlat = Quaternion.identity;
            rightSensorToFlat = Quaternion.identity;
            status = "Not calibrated";
        }

        private FootAngleSample Calculate(FootSide side, Quaternion sensorToFlat, bool sideCalibrated, bool invert)
        {
            var result = new FootAngleSample { side = side, valid = false };
            TrackerPoseSample pose;
            if (!sideCalibrated || !TryCanonicalPose(side, out pose)) return result;

            Quaternion footRotation = pose.rotation * sensorToFlat;
            Vector3 forward = footRotation * Vector3.forward;
            float horizontal = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
            float pitch = Mathf.Atan2(forward.y, Mathf.Max(0.000001f, horizontal)) * Mathf.Rad2Deg;
            if (invert) pitch = -pitch;

            result.trackerId = pose.deviceId;
            result.trackerSample = pose.sampleIndex;
            result.trackerTime = pose.trackerTime;
            result.rawPitchDegrees = pitch;
            result.valid = true;
            return result;
        }

        private bool TryCanonicalPose(FootSide side, out TrackerPoseSample pose)
        {
            pose = default(TrackerPoseSample);
            if (trackerSource == null || !trackerSource.TryGetLatestPose(side, out pose)) return false;
            if (requireGroundFrame && (groundFrame == null || !groundFrame.IsCalibrated)) return false;
            if (groundFrame != null && groundFrame.IsCalibrated) pose = groundFrame.ToCanonical(pose);
            return pose.valid;
        }
    }
}
