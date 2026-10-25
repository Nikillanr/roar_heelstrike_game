using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class HeadsetBootstrap : MonoBehaviour
    {
        public RehabSessionContext session;
        public MetaPassthroughSetup metaSetup;
        public TriggerConnectionServer connection;
        public CrossSpaceRegistration crossSpaceRegistration;
        public CorridorFrame corridor;
        public RomCalibration romCalibration;
        public RehabTaskHost taskHost;
        public HeelStrikeBallTask ballTask;
        public BallSpawnRegion spawnRegion;
        public BallPresenter ballPresenter;
        public HeadsetEventLogger eventLogger;
        public HeadsetStatusPanel statusPanel;

        private void Awake()
        {
            ResolveAndWire();
        }

        [ContextMenu("Resolve and Wire Components")]
        public void ResolveAndWire()
        {
            if (session == null) session = GetComponentInChildren<RehabSessionContext>(true);
            if (metaSetup == null) metaSetup = GetComponentInChildren<MetaPassthroughSetup>(true);
            if (connection == null) connection = GetComponentInChildren<TriggerConnectionServer>(true);
            if (crossSpaceRegistration == null) crossSpaceRegistration = GetComponentInChildren<CrossSpaceRegistration>(true);
            if (corridor == null) corridor = GetComponentInChildren<CorridorFrame>(true);
            if (romCalibration == null) romCalibration = GetComponentInChildren<RomCalibration>(true);
            if (taskHost == null) taskHost = GetComponentInChildren<RehabTaskHost>(true);
            if (ballTask == null) ballTask = GetComponentInChildren<HeelStrikeBallTask>(true);
            if (spawnRegion == null) spawnRegion = GetComponentInChildren<BallSpawnRegion>(true);
            if (ballPresenter == null) ballPresenter = GetComponentInChildren<BallPresenter>(true);
            if (eventLogger == null) eventLogger = GetComponentInChildren<HeadsetEventLogger>(true);
            if (statusPanel == null) statusPanel = GetComponentInChildren<HeadsetStatusPanel>(true);

            if (corridor != null) corridor.headsetSetup = metaSetup;
            if (romCalibration != null)
            {
                romCalibration.headsetSetup = metaSetup;
                romCalibration.corridor = corridor;
            }
            if (spawnRegion != null)
            {
                spawnRegion.headsetSetup = metaSetup;
                spawnRegion.romCalibration = romCalibration;
            }
            if (eventLogger != null) eventLogger.session = session;
            if (ballTask != null)
            {
                ballTask.connection = connection;
                ballTask.romCalibration = romCalibration;
                ballTask.spawnRegion = spawnRegion;
                ballTask.presenter = ballPresenter;
                ballTask.eventLogger = eventLogger;
            }
            if (taskHost != null)
            {
                taskHost.connection = connection;
                taskHost.activeTaskBehaviour = ballTask;
                taskHost.ResolveTask();
            }
            if (statusPanel != null)
            {
                statusPanel.connection = connection;
                statusPanel.taskHost = taskHost;
                statusPanel.ballTask = ballTask;
            }
        }
    }
}
