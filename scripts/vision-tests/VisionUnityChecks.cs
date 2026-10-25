// Copied into an isolated Unity project by Test-VisionUnity.ps1.
using System;
using System.Reflection;
using System.Threading;
using MTPAD.Rehab.Core;
using MTPAD.Rehab.Headset.Vision;
using UnityEditor;
using UnityEngine;

public static class VisionUnityChecks
{
    private static int checks;

    private static void Expect(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++;
        Debug.Log("VISION_CHECK_PASS: " + label);
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetProperty(name).SetValue(target, value, null);
    }

    private static long sequence;

    private static void Apply(VisionStateReceiver receiver, string left, string right)
    {
        string json = "{\"v\":2,\"type\":\"hand_state\",\"sessionId\":\"unity-check\",\"sequence\":" + sequence++
            + ",\"hand\":\"either\",\"state\":\"raised\",\"confidence\":0.9,\"sourceTime\":1,"
            + "\"leftState\":\"" + left + "\",\"rightState\":\"" + right
            + "\",\"leftConfidence\":0.9,\"rightConfidence\":0.9}";
        Type packetType = typeof(VisionStateReceiver).GetNestedType("Packet", BindingFlags.NonPublic);
        object packet = Activator.CreateInstance(packetType);
        packetType.GetField("Kind").SetValue(packet, "state");
        packetType.GetField("Line").SetValue(packet, json);
        packetType.GetField("Receipt").SetValue(packet, RehabClock.MonotonicSeconds);
        typeof(VisionStateReceiver).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(receiver, new[] { packet });
    }

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool("VisionChecksRequested", false))
            EditorApplication.update += WaitForPlay;
    }

    public static void Run()
    {
        SessionState.SetBool("VisionChecksRequested", true);
        EditorApplication.update -= WaitForPlay;
        EditorApplication.update += WaitForPlay;
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying || !Application.isPlaying) return;
        EditorApplication.update -= WaitForPlay;
        SessionState.SetBool("VisionChecksRequested", false);
        ExecuteChecks();
    }

    private static void ExecuteChecks()
    {
        var root = new GameObject("Vision checks");
        root.SetActive(false);
        try
        {
            Camera camera = root.AddComponent<Camera>();
            camera.aspect = 16f / 9f;
            VisionStateReceiver receiver = root.AddComponent<VisionStateReceiver>();
            receiver.port = 0;
            receiver.staleSeconds = 10f;
            VisionBallController controller = root.AddComponent<VisionBallController>();
            controller.receiver = receiver;
            controller.anchor = camera.transform;
            controller.trackingLossGraceSeconds = .01f;
            root.SetActive(true);
            // Active components now run their real Play-mode lifecycle.
            Set(receiver, "Connected", true);
            UnityEngine.Random.InitState(42);

            Apply(receiver, "raised", "lowered");
            Call(controller, "Update");
            Expect(controller.LeftVisible && !controller.RightVisible, "left raise shows only left ball");
            Transform left = root.transform.Find("VisionLeftHandBall");
            Vector3 firstPosition = left.position;
            Vector3 viewport = camera.WorldToViewportPoint(firstPosition);
            Expect(viewport.x >= .18f && viewport.x <= .38f && viewport.y >= .25f && viewport.y <= .55f,
                "left placement is inside its screen region");

            Apply(receiver, "raised", "lowered");
            Call(controller, "Update");
            Expect(left.position == firstPosition, "held hand and heartbeat do not move the ball");

            Apply(receiver, "raised", "raised");
            Call(controller, "Update");
            Transform right = root.transform.Find("VisionRightHandBall");
            viewport = camera.WorldToViewportPoint(right.position);
            Expect(controller.LeftVisible && controller.RightVisible && viewport.x >= .62f && viewport.x <= .82f,
                "both raised shows two balls with right ball on right");
            Vector3 rightPosition = right.position;

            Apply(receiver, "lowered", "raised");
            Call(controller, "Update");
            Expect(!controller.LeftVisible && controller.RightVisible && right.position == rightPosition,
                "lowering left does not hide or move right");

            Apply(receiver, "raised", "raised");
            Call(controller, "Update");
            Expect(root.transform.Find("VisionLeftHandBall") == left && left.position != firstPosition,
                "new raise reuses left object at a new random position");

            Apply(receiver, "", "lowered");
            Expect(receiver.RejectedMessages == 1 && receiver.LeftState == "raised" && receiver.RightState == "raised",
                "invalid per-hand message cannot partially update the other side");

            Apply(receiver, "unknown", "raised");
            Thread.Sleep(30);
            Call(controller, "Update");
            Expect(!controller.LeftVisible && controller.RightVisible, "left tracking loss expires independently");

            receiver.staleSeconds = .01f;
            Thread.Sleep(150);
            Call(receiver, "Update");
            Call(controller, "Update");
            Expect(!controller.Visible && receiver.LeftState == "unknown" && receiver.RightState == "unknown",
                "stale connection hides both sides");

            receiver.staleSeconds = 10f;
            Apply(receiver, "raised", "raised");
            Call(controller, "Update");
            Set(receiver, "ConnectionRevision", receiver.ConnectionRevision + 1);
            Apply(receiver, "unknown", "unknown");
            Call(controller, "Update");
            Expect(!controller.Visible, "new connection cannot revive previous hand states");

            Call(controller, "OnDisable");
            Call(receiver, "OnDisable");
            Debug.Log("VISION_UNITY_CHECKS_OK: " + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
