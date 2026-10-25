using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class HeadsetStatusPanel : MonoBehaviour
    {
        public TriggerConnectionServer connection;
        public RehabTaskHost taskHost;
        public HeelStrikeBallTask ballTask;
        public bool showInEditorAndBuild;

        private void OnGUI()
        {
            if (!showInEditorAndBuild) return;
            GUI.Box(new Rect(12, 12, 520, 135), "mTPAD Headset Template");
            GUILayout.BeginArea(new Rect(24, 38, 490, 100));
            GUILayout.Label("Trigger: " + (connection != null ? connection.Status : "Not assigned"));
            GUILayout.Label("Task: " + (taskHost != null ? taskHost.ActiveTaskId + " / " + taskHost.Status : "Not assigned"));
            GUILayout.Label("Ball: " + (ballTask != null ? ballTask.Status : "Not assigned"));
            GUILayout.Label("Spawns: " + (ballTask != null ? ballTask.SpawnCount.ToString() : "0"));
            GUILayout.EndArea();
        }
    }
}
