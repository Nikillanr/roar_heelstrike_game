using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class CorridorFrame : MonoBehaviour
    {
        public MetaPassthroughSetup headsetSetup;
        [Min(0.5f)] public float lengthMeters = 5f;
        [Min(0.25f)] public float widthMeters = 1.5f;

        [SerializeField] private bool placed;
        [SerializeField] private Vector3 origin;
        [SerializeField] private Quaternion rotation = Quaternion.identity;
        [SerializeField] private int revision;

        public bool Placed { get { return placed; } }
        public Vector3 Origin { get { return origin; } }
        public Quaternion Rotation { get { return rotation; } }
        public int Revision { get { return revision; } }

        [ContextMenu("Place Corridor From Current Head")]
        public void PlaceFromCurrentHead()
        {
            Transform head = headsetSetup != null ? headsetSetup.HeadTransform : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            origin = new Vector3(head.position.x, 0f, head.position.z);
            rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            placed = true;
            revision++;
        }

        [ContextMenu("Clear Corridor")]
        public void ClearCorridor()
        {
            placed = false;
            origin = Vector3.zero;
            rotation = Quaternion.identity;
            revision++;
        }
    }
}
