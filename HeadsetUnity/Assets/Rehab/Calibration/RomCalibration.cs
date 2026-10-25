using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class RomCalibration : MonoBehaviour
    {
        public MetaPassthroughSetup headsetSetup;
        public CorridorFrame corridor;

        [Header("Calibrated head ROM")]
        [SerializeField] private bool calibrated;
        [SerializeField] private bool collecting;
        [SerializeField] private Quaternion baseRotation = Quaternion.identity;
        [SerializeField] private float flexionDegrees = 30f;
        [SerializeField] private float extensionDegrees = 30f;
        [SerializeField] private float leftRotationDegrees = 30f;
        [SerializeField] private float rightRotationDegrees = 30f;

        public bool Calibrated { get { return calibrated; } }
        public Quaternion BaseRotation { get { return baseRotation; } }
        public float FlexionDegrees { get { return flexionDegrees; } }
        public float ExtensionDegrees { get { return extensionDegrees; } }
        public float LeftRotationDegrees { get { return leftRotationDegrees; } }
        public float RightRotationDegrees { get { return rightRotationDegrees; } }

        private void Update()
        {
            if (!collecting) return;
            Transform head = GetHead();
            if (head == null) return;
            Vector3 localForward = Quaternion.Inverse(baseRotation) * head.forward;
            float yaw = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
            float horizontal = Mathf.Sqrt(localForward.x * localForward.x + localForward.z * localForward.z);
            float pitch = Mathf.Atan2(localForward.y, Mathf.Max(0.000001f, horizontal)) * Mathf.Rad2Deg;
            if (yaw < 0f) leftRotationDegrees = Mathf.Max(leftRotationDegrees, -yaw);
            else rightRotationDegrees = Mathf.Max(rightRotationDegrees, yaw);
            if (pitch < 0f) flexionDegrees = Mathf.Max(flexionDegrees, -pitch);
            else extensionDegrees = Mathf.Max(extensionDegrees, pitch);
        }

        [ContextMenu("Capture ROM Center")]
        public void CaptureCenter()
        {
            Transform head = GetHead();
            if (head == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            baseRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            flexionDegrees = extensionDegrees = leftRotationDegrees = rightRotationDegrees = 0f;
            calibrated = false;
        }

        [ContextMenu("Start ROM Collection")]
        public void StartCollection()
        {
            CaptureCenter();
            collecting = true;
        }

        [ContextMenu("Finish ROM Collection")]
        public void FinishCollection()
        {
            collecting = false;
            calibrated = flexionDegrees > 0.1f && extensionDegrees > 0.1f
                && leftRotationDegrees > 0.1f && rightRotationDegrees > 0.1f;
        }

        [ContextMenu("Use 30 Degree Test ROM")]
        public void UseTestCalibration()
        {
            Transform head = GetHead();
            if (head != null)
            {
                Vector3 forward = head.forward;
                forward.y = 0f;
                baseRotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward, Vector3.up);
            }
            flexionDegrees = extensionDegrees = leftRotationDegrees = rightRotationDegrees = 30f;
            collecting = false;
            calibrated = true;
        }

        public Quaternion GetPlacementBaseRotation()
        {
            if (calibrated) return baseRotation;
            if (corridor != null && corridor.Placed) return corridor.Rotation;
            Transform head = GetHead();
            if (head == null) return Quaternion.identity;
            Vector3 forward = head.forward;
            forward.y = 0f;
            return Quaternion.LookRotation(forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward, Vector3.up);
        }

        private Transform GetHead()
        {
            return headsetSetup != null ? headsetSetup.HeadTransform : Camera.main != null ? Camera.main.transform : null;
        }
    }
}
