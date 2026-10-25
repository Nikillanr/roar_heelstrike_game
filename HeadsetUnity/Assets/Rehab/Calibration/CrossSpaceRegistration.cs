using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class CrossSpaceRegistration : MonoBehaviour
    {
        [Header("Two corresponding points")]
        public Vector3 pcPointA;
        public Vector3 pcPointB = Vector3.forward;
        public Vector3 headsetPointA;
        public Vector3 headsetPointB = Vector3.forward;

        [Header("Solved PC to headset transform")]
        [SerializeField] private bool valid;
        [SerializeField] private Quaternion pcToHeadsetRotation = Quaternion.identity;
        [SerializeField] private Vector3 pcToHeadsetTranslation;
        [SerializeField] private int revision;
        [SerializeField] private float validationErrorMeters;

        public bool Valid { get { return valid; } }
        public int Revision { get { return revision; } }
        public float ValidationErrorMeters { get { return validationErrorMeters; } }

        [ContextMenu("Solve Registration From Points")]
        public void SolveFromPoints()
        {
            Vector3 pcDirection = pcPointB - pcPointA;
            Vector3 headsetDirection = headsetPointB - headsetPointA;
            pcDirection.y = 0f;
            headsetDirection.y = 0f;
            if (pcDirection.sqrMagnitude < 0.000001f || headsetDirection.sqrMagnitude < 0.000001f)
            {
                valid = false;
                return;
            }

            float yaw = Vector3.SignedAngle(pcDirection.normalized, headsetDirection.normalized, Vector3.up);
            pcToHeadsetRotation = Quaternion.AngleAxis(yaw, Vector3.up);
            pcToHeadsetTranslation = headsetPointA - pcToHeadsetRotation * pcPointA;
            validationErrorMeters = Vector3.Distance(TransformPoint(pcPointB), headsetPointB);
            valid = true;
            revision++;
        }

        [ContextMenu("Clear Registration")]
        public void ClearRegistration()
        {
            valid = false;
            pcToHeadsetRotation = Quaternion.identity;
            pcToHeadsetTranslation = Vector3.zero;
            validationErrorMeters = 0f;
            revision++;
        }

        public Vector3 TransformPoint(Vector3 pcPoint)
        {
            return pcToHeadsetRotation * pcPoint + pcToHeadsetTranslation;
        }

        public Quaternion TransformRotation(Quaternion pcRotation)
        {
            return pcToHeadsetRotation * pcRotation;
        }
    }
}
