using UnityEngine;

namespace MTPAD.Rehab.Headset.Vision
{
    // Only used by Vision_DesktopDemo; never auto-installs into the Quest/Vive scenes.
    public sealed class VisionDesktopDemo : MonoBehaviour
    {
        private VisionStateReceiver receiver;
        private VisionBallController controller;

        private void Awake()
        {
            var cameraObject = new GameObject("Desktop Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.07f);
            camera.nearClipPlane = 0.05f;
            receiver = gameObject.AddComponent<VisionStateReceiver>();
            controller = gameObject.AddComponent<VisionBallController>();
            controller.receiver = receiver;
            controller.anchor = cameraObject.transform;
        }

        private void OnGUI()
        {
            if (receiver == null) return;
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(760, Screen.width - 32), 170), GUI.skin.box);
            GUILayout.Label("VISION INPUT EXPERIMENT");
            GUILayout.Label("Left hand: left ball | Right hand: right ball | Both raised: both balls");
            GUILayout.Label(receiver.Status ?? "Starting");
            GUILayout.Label("Left: " + receiver.LeftState
                + " | confidence: " + (receiver.HasState ? receiver.LeftConfidence.ToString("F2") : "--")
                + " | ball " + (controller.LeftVisible ? "visible" : "hidden"));
            GUILayout.Label("Right: " + receiver.RightState
                + " | confidence: " + (receiver.HasState ? receiver.RightConfidence.ToString("F2") : "--")
                + " | ball " + (controller.RightVisible ? "visible" : "hidden"));
            GUILayout.Label("A new raise picks a random spot on that side. Hold: stays put. Lower: hides.");
            GUILayout.Label("Python target: 127.0.0.1:7778 | rejected messages: " + receiver.RejectedMessages);
            GUILayout.EndArea();
        }
    }
}
