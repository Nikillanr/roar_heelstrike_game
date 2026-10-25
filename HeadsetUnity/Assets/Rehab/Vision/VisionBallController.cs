using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisionBallController : MonoBehaviour
    {
        public VisionStateReceiver receiver;
        public Transform anchor;
        public Vector3 localPosition = new Vector3(0f, 0f, 2f);
        public float diameterMeters = 0.25f;
        public float trackingLossGraceSeconds = 0.6f;
        public Color color = new Color(0.15f, 0.95f, 0.35f);
        public Material materialTemplate;
        [Tooltip("Horizontal viewport interval for the left ball; mirrored for the right ball.")]
        public Vector2 horizontalViewportRange = new Vector2(0.18f, 0.38f);
        public Vector2 verticalViewportRange = new Vector2(0.25f, 0.55f);
        public bool LeftVisible { get { return IsVisible(left); } }
        public bool RightVisible { get { return IsVisible(right); } }
        public bool Visible { get { return LeftVisible || RightVisible; } }

        private sealed class HandBall
        {
            public GameObject Ball;
            public Material Material;
            public double LastConfirmedAt = double.NegativeInfinity;
            public bool LastRaised;
        }

        private readonly HandBall left = new HandBall();
        private readonly HandBall right = new HandBall();
        private int connectionRevision = -1;

        private static bool IsVisible(HandBall hand)
        {
            return hand.Ball != null && hand.Ball.activeSelf;
        }

        private void OnEnable()
        {
            connectionRevision = -1;
            ResetTracking(left);
            ResetTracking(right);
        }

        private void Update()
        {
            bool fresh = receiver != null && receiver.isActiveAndEnabled && receiver.Connected && receiver.HasState;
            if (!fresh || connectionRevision != receiver.ConnectionRevision)
            {
                ResetTracking(left);
                ResetTracking(right);
                connectionRevision = receiver != null ? receiver.ConnectionRevision : -1;
            }
            UpdateHand(left, true, fresh, fresh ? receiver.LeftState : "unknown");
            UpdateHand(right, false, fresh, fresh ? receiver.RightState : "unknown");
        }

        private void UpdateHand(HandBall hand, bool isLeft, bool fresh, string state)
        {
            if (fresh && state != "unknown")
            {
                hand.LastConfirmedAt = receiver.ReceivedAt;
                hand.LastRaised = state == "raised";
            }
            bool show = fresh && hand.LastRaised;
            if (show && state == "unknown")
                show = RehabClock.MonotonicSeconds - hand.LastConfirmedAt <= Mathf.Max(0f, trackingLossGraceSeconds);
            if (show && !IsVisible(hand))
            {
                if (hand.Ball == null) CreateBall(hand, isLeft);
                PositionBall(hand, isLeft);
            }
            if (hand.Ball != null) hand.Ball.SetActive(show);
        }

        private void CreateBall(HandBall hand, bool isLeft)
        {
            hand.Ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hand.Ball.name = isLeft ? "VisionLeftHandBall" : "VisionRightHandBall";
            hand.Ball.transform.SetParent(anchor != null ? anchor : transform, false);
            hand.Ball.transform.localScale = Vector3.one * Mathf.Max(0.01f, diameterMeters);
            Destroy(hand.Ball.GetComponent<Collider>());
            hand.Material = materialTemplate != null
                ? new Material(materialTemplate)
                : new Material(Shader.Find("Unlit/Color"));
            hand.Material.color = color;
            hand.Ball.GetComponent<Renderer>().sharedMaterial = hand.Material;
        }

        private void PositionBall(HandBall hand, bool isLeft)
        {
            Camera camera = anchor != null ? anchor.GetComponent<Camera>() : null;
            if (camera == null) camera = Camera.main;
            if (camera != null)
            {
                float minX = Mathf.Clamp(Mathf.Min(horizontalViewportRange.x, horizontalViewportRange.y), 0.05f, 0.45f);
                float maxX = Mathf.Clamp(Mathf.Max(horizontalViewportRange.x, horizontalViewportRange.y), minX, 0.45f);
                float x = Random.Range(minX, maxX);
                if (!isLeft) x = 1f - x;
                float minY = Mathf.Clamp01(Mathf.Min(verticalViewportRange.x, verticalViewportRange.y));
                float maxY = Mathf.Clamp01(Mathf.Max(verticalViewportRange.x, verticalViewportRange.y));
                hand.Ball.transform.position = camera.ViewportToWorldPoint(new Vector3(x, Random.Range(minY, maxY), Mathf.Max(0.5f, localPosition.z)));
            }
            else
            {
                hand.Ball.transform.localPosition = localPosition
                    + new Vector3((isLeft ? -1f : 1f) * Random.Range(0.4f, 0.8f), Random.Range(-0.3f, 0.3f), 0f);
            }
        }

        private static void ResetTracking(HandBall hand)
        {
            hand.LastConfirmedAt = double.NegativeInfinity;
            hand.LastRaised = false;
        }

        private void Release(HandBall hand)
        {
            if (hand.Ball != null) Destroy(hand.Ball);
            if (hand.Material != null) Destroy(hand.Material);
            hand.Ball = null;
            hand.Material = null;
            ResetTracking(hand);
        }

        private void OnDisable()
        {
            Release(left);
            Release(right);
        }
    }
}
