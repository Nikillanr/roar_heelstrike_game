using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class GroundFrameCalibration : MonoBehaviour
    {
        public UnityXrTrackerSource trackerSource;

        [Header("Canonical ground frame")]
        [SerializeField] private bool calibrated;
        [SerializeField] private Vector3 rawOrigin;
        [SerializeField] private Quaternion rawToCanonicalRotation = Quaternion.identity;
        [SerializeField] private int revision;
        [SerializeField] private string status = "Not calibrated";

        public bool IsCalibrated { get { return calibrated; } }
        public int Revision { get { return revision; } }
        public Vector3 RawOrigin { get { return rawOrigin; } }
        public Quaternion RawToCanonicalRotation { get { return rawToCanonicalRotation; } }
        public string Status { get { return status; } }

        [ContextMenu("Capture Ground Frame")]
        public void CaptureGroundFrame()
        {
            TrackerPoseSample left;
            TrackerPoseSample right;
            if (trackerSource == null
                || !trackerSource.TryGetLatestPose(FootSide.Left, out left)
                || !trackerSource.TryGetLatestPose(FootSide.Right, out right))
            {
                status = "Both foot trackers must be tracking.";
                return;
            }

            Vector3 center = (left.position + right.position) * 0.5f;
            rawOrigin = new Vector3(center.x, 0f, center.z);
            Vector3 forward = left.rotation * Vector3.forward + right.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            Quaternion rawHeading = Quaternion.LookRotation(forward.normalized, Vector3.up);
            rawToCanonicalRotation = Quaternion.Inverse(rawHeading);
            calibrated = true;
            revision++;
            status = "Ground frame captured (Unity/OpenVR Y-up).";
        }

        [ContextMenu("Clear Ground Frame")]
        public void ClearGroundFrame()
        {
            calibrated = false;
            rawOrigin = Vector3.zero;
            rawToCanonicalRotation = Quaternion.identity;
            revision++;
            status = "Not calibrated";
        }

        public TrackerPoseSample ToCanonical(TrackerPoseSample raw)
        {
            if (!calibrated || !raw.valid) return raw;
            raw.position = rawToCanonicalRotation * (raw.position - rawOrigin);
            raw.rotation = rawToCanonicalRotation * raw.rotation;
            return raw;
        }
    }
}
