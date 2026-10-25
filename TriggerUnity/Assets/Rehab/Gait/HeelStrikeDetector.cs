using System;
using System.Globalization;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class HeelStrikeDetector : MonoBehaviour
    {
        public const string AlgorithmVersion = "SagittalPitchPeak-Causal-v1";

        public FootAngleEstimator angleEstimator;
        public GroundFrameCalibration groundFrame;

        [Header("Detector")]
        public bool detectorEnabled = true;
        public bool leftEnabled = true;
        public bool rightEnabled = true;
        [Range(0.5f, 20f)] public float cutoffHz = 6f;
        [Range(0.5f, 30f)] public float prominenceDegrees = 5f;
        [Range(0.1f, 2f)] public float refractorySeconds = 0.70f;
        [Range(1, 10)] public int confirmationSamples = 3;
        [Range(0.05f, 10f)] public float confirmationDropDegrees = 0.5f;
        [Range(0.05f, 1f)] public float maximumSampleGapSeconds = 0.15f;

        [Header("Live status")]
        [SerializeField] private float leftFilteredPitch;
        [SerializeField] private float rightFilteredPitch;
        [SerializeField] private bool leftTrackingValid;
        [SerializeField] private bool rightTrackingValid;
        [SerializeField] private string leftState = "Waiting";
        [SerializeField] private string rightState = "Waiting";
        [SerializeField] private int detectionCount;
        [SerializeField] private string lastEventId = "";
        [SerializeField] private string lastEventSide = "";

        private readonly FootState left = new FootState(FootSide.Left);
        private readonly FootState right = new FootState(FootSide.Right);
        private long detectorSequence;
        private long eventSequence;

        public event Action<HeelStrikeEvent> HeelStrikeDetected;

        public float LeftFilteredPitch { get { return leftFilteredPitch; } }
        public float RightFilteredPitch { get { return rightFilteredPitch; } }
        public bool LeftTrackingValid { get { return leftTrackingValid; } }
        public bool RightTrackingValid { get { return rightTrackingValid; } }
        public string LeftState { get { return leftState; } }
        public string RightState { get { return rightState; } }
        public int DetectionCount { get { return detectionCount; } }
        public string LastEventId { get { return lastEventId; } }
        public string LastEventSide { get { return lastEventSide; } }

        private sealed class FootState
        {
            public readonly FootSide Side;
            public readonly CausalLowPassFilter Filter = new CausalLowPassFilter();
            public bool HasSignal;
            public long LastTrackerSample;
            public double LastSampleTime;
            public double LastAcceptedPeakTime = double.NegativeInfinity;
            public float PreviousFiltered;
            public float Trough;
            public bool Rising;
            public float Peak;
            public float PeakRaw;
            public double PeakTime;
            public long PeakTrackerSample;
            public string PeakTrackerId = "";
            public long PeakDetectorSample;
            public int FallingSamples;

            public FootState(FootSide side) { Side = side; }

            public void ResetSignal()
            {
                HasSignal = false;
                Rising = false;
                FallingSamples = 0;
                Filter.Reset();
            }
        }

        private void Update()
        {
            if (!detectorEnabled || angleEstimator == null) return;
            Process(left, leftEnabled);
            Process(right, rightEnabled);
        }

        [ContextMenu("Reset Detection Counts")]
        public void ResetCounts()
        {
            detectionCount = 0;
            eventSequence = 0;
            lastEventId = "";
            lastEventSide = "";
            left.LastAcceptedPeakTime = double.NegativeInfinity;
            right.LastAcceptedPeakTime = double.NegativeInfinity;
        }

        private void Process(FootState foot, bool sideEnabled)
        {
            FootAngleSample sample;
            bool valid = angleEstimator.TryGetLatest(foot.Side, out sample);
            SetTrackingValid(foot.Side, valid);
            if (!valid)
            {
                foot.ResetSignal();
                SetState(foot.Side, "Waiting for calibrated tracker");
                return;
            }
            if (sample.trackerSample == foot.LastTrackerSample) return;
            foot.LastTrackerSample = sample.trackerSample;
            detectorSequence++;

            double deltaTime = foot.HasSignal
                ? sample.trackerTime - foot.LastSampleTime
                : 1d / 90d;
            if (deltaTime <= 0d || deltaTime > maximumSampleGapSeconds)
            {
                foot.ResetSignal();
                deltaTime = 1d / 90d;
            }

            float filtered = foot.Filter.Process(sample.rawPitchDegrees, deltaTime, cutoffHz);
            foot.LastSampleTime = sample.trackerTime;
            SetFiltered(foot.Side, filtered);
            if (!foot.HasSignal)
            {
                foot.HasSignal = true;
                foot.PreviousFiltered = filtered;
                foot.Trough = filtered;
                SetState(foot.Side, sideEnabled ? "Seeking rise" : "Side disabled");
                return;
            }

            float slope = (filtered - foot.PreviousFiltered) / Mathf.Max(0.0001f, (float)deltaTime);
            foot.PreviousFiltered = filtered;
            if (!foot.Rising)
            {
                if (filtered < foot.Trough) foot.Trough = filtered;
                if (sideEnabled && slope > 0f && filtered - foot.Trough >= Mathf.Min(1f, prominenceDegrees * 0.25f))
                {
                    foot.Rising = true;
                    CapturePeak(foot, sample, filtered);
                    SetState(foot.Side, "Rising");
                }
                else SetState(foot.Side, sideEnabled ? "Seeking rise" : "Side disabled");
                return;
            }

            if (filtered > foot.Peak)
            {
                CapturePeak(foot, sample, filtered);
                SetState(foot.Side, "Rising");
                return;
            }

            if (slope < 0f) foot.FallingSamples++;
            else if (slope > 0f) foot.FallingSamples = 0;
            if (foot.FallingSamples < Mathf.Max(1, confirmationSamples)
                || foot.Peak - filtered < confirmationDropDegrees)
            {
                SetState(foot.Side, "Confirming peak");
                return;
            }

            float prominence = foot.Peak - foot.Trough;
            bool outsideRefractory = foot.PeakTime - foot.LastAcceptedPeakTime >= refractorySeconds;
            if (sideEnabled && prominence >= prominenceDegrees && outsideRefractory)
            {
                foot.LastAcceptedPeakTime = foot.PeakTime;
                Emit(foot, prominence);
            }
            else SetState(foot.Side, prominence < prominenceDegrees
                ? "Rejected: low prominence"
                : "Rejected: refractory");

            foot.Rising = false;
            foot.FallingSamples = 0;
            foot.Trough = filtered;
        }

        private void CapturePeak(FootState foot, FootAngleSample sample, float filtered)
        {
            foot.Peak = filtered;
            foot.PeakRaw = sample.rawPitchDegrees;
            foot.PeakTime = sample.trackerTime;
            foot.PeakTrackerSample = sample.trackerSample;
            foot.PeakTrackerId = sample.trackerId;
            foot.PeakDetectorSample = detectorSequence;
            foot.FallingSamples = 0;
        }

        private void Emit(FootState foot, float prominence)
        {
            detectionCount++;
            eventSequence++;
            double detectionTime = RehabClock.MonotonicSeconds;
            var value = new HeelStrikeEvent
            {
                eventId = "HS_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)
                    + "_" + foot.Side.Token() + "_" + eventSequence.ToString("D6", CultureInfo.InvariantCulture),
                side = foot.Side,
                trackerId = foot.PeakTrackerId,
                trackerSample = foot.PeakTrackerSample,
                detectorSample = foot.PeakDetectorSample,
                trackerPcTime = foot.PeakTime,
                peakPcTime = foot.PeakTime,
                detectedPcTime = detectionTime,
                rawPeakDegrees = foot.PeakRaw,
                filteredPeakDegrees = foot.Peak,
                prominenceDegrees = prominence,
                algorithm = AlgorithmVersion,
                groundRevision = groundFrame != null ? groundFrame.Revision : 0
            };
            lastEventId = value.eventId;
            lastEventSide = value.side.Token();
            SetState(foot.Side, "Heel strike detected");
            Action<HeelStrikeEvent> handler = HeelStrikeDetected;
            if (handler != null) handler(value);
        }

        private void SetFiltered(FootSide side, float value)
        {
            if (side == FootSide.Left) leftFilteredPitch = value;
            else rightFilteredPitch = value;
        }

        private void SetTrackingValid(FootSide side, bool value)
        {
            if (side == FootSide.Left) leftTrackingValid = value;
            else rightTrackingValid = value;
        }

        private void SetState(FootSide side, string value)
        {
            if (side == FootSide.Left) leftState = value;
            else rightState = value;
        }
    }
}
