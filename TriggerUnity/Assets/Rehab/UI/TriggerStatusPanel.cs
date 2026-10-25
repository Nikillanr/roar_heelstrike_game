using System.Collections.Generic;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class TriggerStatusPanel : MonoBehaviour
    {
        public UnityXrTrackerSource trackerSource;
        public FootAngleEstimator angleEstimator;
        public HeelStrikeDetector detector;
        public HeadsetConnectionClient connection;
        public bool showPanel = true;
        public int graphSamples = 300;
        public float graphMinimumDegrees = -40f;
        public float graphMaximumDegrees = 40f;

        private readonly List<float> leftHistory = new List<float>();
        private readonly List<float> rightHistory = new List<float>();
        private readonly List<bool> leftEvents = new List<bool>();
        private readonly List<bool> rightEvents = new List<bool>();
        private HeelStrikeDetector subscribedDetector;
        private bool pendingLeftEvent;
        private bool pendingRightEvent;
        private Texture2D white;

        private void OnEnable()
        {
            RefreshSubscriptions();
        }

        private void OnDisable()
        {
            if (subscribedDetector != null) subscribedDetector.HeelStrikeDetected -= HandleHeelStrike;
            subscribedDetector = null;
        }

        [ContextMenu("Refresh Event Subscription")]
        public void RefreshSubscriptions()
        {
            if (subscribedDetector != null) subscribedDetector.HeelStrikeDetected -= HandleHeelStrike;
            subscribedDetector = detector;
            if (subscribedDetector != null) subscribedDetector.HeelStrikeDetected += HandleHeelStrike;
        }

        private void Update()
        {
            if (angleEstimator == null) return;
            Append(leftHistory, leftEvents, angleEstimator.LeftPitchDegrees, pendingLeftEvent);
            Append(rightHistory, rightEvents, angleEstimator.RightPitchDegrees, pendingRightEvent);
            pendingLeftEvent = false;
            pendingRightEvent = false;
        }

        private void OnGUI()
        {
            if (!showPanel) return;
            GUI.Box(new Rect(12, 12, 540, 265), "mTPAD Trigger Template");
            GUILayout.BeginArea(new Rect(24, 38, 516, 105));
            GUILayout.Label("Headset: " + (connection != null ? connection.Status : "Not assigned"));
            GUILayout.Label("Left: " + (trackerSource != null ? trackerSource.LeftResolvedDevice : "Not assigned")
                + "   angle=" + (angleEstimator != null ? angleEstimator.LeftPitchDegrees.ToString("F2") : "--"));
            GUILayout.Label("Right: " + (trackerSource != null ? trackerSource.RightResolvedDevice : "Not assigned")
                + "   angle=" + (angleEstimator != null ? angleEstimator.RightPitchDegrees.ToString("F2") : "--"));
            GUILayout.Label("Events: " + (detector != null ? detector.DetectionCount.ToString() : "0")
                + "   last=" + (detector != null ? detector.LastEventSide : ""));
            GUILayout.EndArea();

            GUI.Label(new Rect(24, 128, 516, 20), "Blue: left angle   Orange: right angle   Vertical marks: heel strikes");
            Rect graph = new Rect(24, 150, 516, 112);
            GUI.Box(graph, GUIContent.none);
            DrawSeries(graph, leftHistory, new Color(0.2f, 0.8f, 1f, 1f));
            DrawSeries(graph, rightHistory, new Color(1f, 0.55f, 0.2f, 1f));
            DrawEventMarkers(graph, leftEvents, new Color(0.2f, 0.8f, 1f, 0.85f));
            DrawEventMarkers(graph, rightEvents, new Color(1f, 0.55f, 0.2f, 0.85f));
        }

        private void Append(List<float> values, List<bool> events, float value, bool eventMarker)
        {
            values.Add(value);
            events.Add(eventMarker);
            int capacity = Mathf.Max(20, graphSamples);
            if (values.Count > capacity) values.RemoveRange(0, values.Count - capacity);
            if (events.Count > capacity) events.RemoveRange(0, events.Count - capacity);
        }

        private void DrawSeries(Rect rect, List<float> values, Color color)
        {
            if (values.Count < 2) return;
            EnsureTexture();
            Color previous = GUI.color;
            GUI.color = color;
            for (int i = 1; i < values.Count; i++)
            {
                float x0 = rect.x + (i - 1) * rect.width / Mathf.Max(1, graphSamples - 1);
                float x1 = rect.x + i * rect.width / Mathf.Max(1, graphSamples - 1);
                float y0 = MapY(rect, values[i - 1]);
                float y1 = MapY(rect, values[i]);
                DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), 2f);
            }
            GUI.color = previous;
        }

        private void DrawEventMarkers(Rect rect, List<bool> events, Color color)
        {
            EnsureTexture();
            Color previous = GUI.color;
            GUI.color = color;
            for (int i = 0; i < events.Count; i++)
            {
                if (!events[i]) continue;
                float x = rect.x + i * rect.width / Mathf.Max(1, graphSamples - 1);
                GUI.DrawTexture(new Rect(x - 1f, rect.y + 3f, 2f, rect.height - 6f), white);
            }
            GUI.color = previous;
        }

        private void HandleHeelStrike(HeelStrikeEvent value)
        {
            if (value == null) return;
            if (value.side == FootSide.Left) pendingLeftEvent = true;
            else pendingRightEvent = true;
        }

        private float MapY(Rect rect, float value)
        {
            float normalized = Mathf.InverseLerp(graphMinimumDegrees, graphMaximumDegrees, value);
            return Mathf.Lerp(rect.yMax - 3f, rect.y + 3f, normalized);
        }

        private void DrawLine(Vector2 from, Vector2 to, float width)
        {
            Matrix4x4 matrix = GUI.matrix;
            float angle = Vector2.SignedAngle(Vector2.right, to - from);
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, Vector2.Distance(from, to), width), white);
            GUI.matrix = matrix;
        }

        private void EnsureTexture()
        {
            if (white != null) return;
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        private void OnDestroy()
        {
            if (white != null) Destroy(white);
        }
    }
}
