using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class RehabTaskHost : MonoBehaviour
    {
        public TriggerConnectionServer connection;
        [Tooltip("Assign a MonoBehaviour that implements IRehabTask.")]
        public MonoBehaviour activeTaskBehaviour;
        public bool startTaskOnPlay = true;

        [Header("Live status")]
        [SerializeField] private string activeTaskId = "None";
        [SerializeField] private string status = "Stopped";

        private IRehabTask activeTask;

        public string ActiveTaskId { get { return activeTaskId; } }
        public string Status { get { return status; } }

        private void OnEnable()
        {
            ResolveTask();
            if (connection != null) connection.HeelStrikeReceived += HandleHeelStrike;
        }

        private void Start()
        {
            if (startTaskOnPlay && activeTask != null)
            {
                activeTask.StartTask();
                status = "Running";
            }
        }

        private void OnDisable()
        {
            if (connection != null) connection.HeelStrikeReceived -= HandleHeelStrike;
            if (activeTask != null && activeTask.IsRunning) activeTask.StopTask();
        }

        [ContextMenu("Resolve Active Task")]
        public void ResolveTask()
        {
            activeTask = activeTaskBehaviour as IRehabTask;
            activeTaskId = activeTask != null ? activeTask.TaskId : "None";
            status = activeTask != null ? "Ready" : "Assigned component does not implement IRehabTask";
        }

        [ContextMenu("Start Active Task")]
        public void StartActiveTask()
        {
            ResolveTask();
            if (activeTask == null) return;
            activeTask.StartTask();
            status = "Running";
        }

        [ContextMenu("Stop Active Task")]
        public void StopActiveTask()
        {
            if (activeTask == null) return;
            activeTask.StopTask();
            status = "Stopped";
        }

        private void HandleHeelStrike(HeelStrikeEvent value, double receiptTime)
        {
            if (activeTask != null)
            {
                activeTask.HandleHeelStrike(value, receiptTime);
                return;
            }
            if (connection != null)
            {
                connection.SendAck(new HeelStrikeAck
                {
                    eventId = value != null ? value.eventId : "",
                    accepted = false,
                    reason = "no_active_task",
                    headsetReceiptTime = receiptTime
                });
            }
        }
    }
}
