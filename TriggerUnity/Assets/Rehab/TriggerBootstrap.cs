using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class TriggerBootstrap : MonoBehaviour
    {
        public RehabSessionContext session;
        public UnityXrTrackerSource trackerSource;
        public GroundFrameCalibration groundFrame;
        public FootAngleEstimator angleEstimator;
        public HeelStrikeDetector heelStrikeDetector;
        public HeadsetConnectionClient headsetConnection;
        public TrackerRecorder trackerRecorder;
        public TriggerEventLogger eventLogger;
        public HeelStrikeTriggerController triggerController;
        public TriggerStatusPanel statusPanel;

        private void Awake()
        {
            ResolveAndWire();
        }

        [ContextMenu("Resolve and Wire Components")]
        public void ResolveAndWire()
        {
            if (session == null) session = GetComponentInChildren<RehabSessionContext>(true);
            if (trackerSource == null) trackerSource = GetComponentInChildren<UnityXrTrackerSource>(true);
            if (groundFrame == null) groundFrame = GetComponentInChildren<GroundFrameCalibration>(true);
            if (angleEstimator == null) angleEstimator = GetComponentInChildren<FootAngleEstimator>(true);
            if (heelStrikeDetector == null) heelStrikeDetector = GetComponentInChildren<HeelStrikeDetector>(true);
            if (headsetConnection == null) headsetConnection = GetComponentInChildren<HeadsetConnectionClient>(true);
            if (trackerRecorder == null) trackerRecorder = GetComponentInChildren<TrackerRecorder>(true);
            if (eventLogger == null) eventLogger = GetComponentInChildren<TriggerEventLogger>(true);
            if (triggerController == null) triggerController = GetComponentInChildren<HeelStrikeTriggerController>(true);
            if (statusPanel == null) statusPanel = GetComponentInChildren<TriggerStatusPanel>(true);

            if (groundFrame != null) groundFrame.trackerSource = trackerSource;
            if (angleEstimator != null)
            {
                angleEstimator.trackerSource = trackerSource;
                angleEstimator.groundFrame = groundFrame;
            }
            if (heelStrikeDetector != null)
            {
                heelStrikeDetector.angleEstimator = angleEstimator;
                heelStrikeDetector.groundFrame = groundFrame;
            }
            if (trackerRecorder != null)
            {
                trackerRecorder.session = session;
                trackerRecorder.trackerSource = trackerSource;
                trackerRecorder.groundFrame = groundFrame;
            }
            if (eventLogger != null) eventLogger.session = session;
            if (triggerController != null)
            {
                triggerController.detector = heelStrikeDetector;
                triggerController.headsetConnection = headsetConnection;
                triggerController.eventLogger = eventLogger;
            }
            if (statusPanel != null)
            {
                statusPanel.trackerSource = trackerSource;
                statusPanel.angleEstimator = angleEstimator;
                statusPanel.detector = heelStrikeDetector;
                statusPanel.connection = headsetConnection;
                statusPanel.RefreshSubscriptions();
            }
        }
    }
}
