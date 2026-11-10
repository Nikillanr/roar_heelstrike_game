// Lightweight receiver checks outside Unity. The JSON adapter below uses .NET's
// serializer; this does not validate Unity JsonUtility or rendering behavior.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using MTPAD.Rehab.Headset.Vision;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
    public static class Debug { public static void LogError(object message, object context) { } }
    public static class JsonUtility
    {
        public static string ToJson(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static void FromJsonOverwrite(string json, object result)
        {
            var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            foreach (var field in result.GetType().GetFields())
                if (values.ContainsKey(field.Name)) field.SetValue(result, Convert.ChangeType(values[field.Name], field.FieldType));
        }
    }
}

public static class ReceiverHarness
{
    private static void Call(object target, string name)
    {
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    }

    private static void Expect(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }

    private static void Pump(VisionStateReceiver receiver)
    {
        Thread.Sleep(60);
        Call(receiver, "Update");
    }

    private static string Message(long sequence, string state)
    {
        return "{\"v\":1,\"type\":\"hand_state\",\"sessionId\":\"test\",\"sequence\":" + sequence
            + ",\"hand\":\"either\",\"state\":\"" + state + "\",\"confidence\":0.9,\"sourceTime\":1}\n";
    }

    private static void Send(NetworkStream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static void Run()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var receiver = new VisionStateReceiver { port = port, staleSeconds = .5f };
        Call(receiver, "OnEnable");
        try
        {
            using (var client = new TcpClient("127.0.0.1", port))
            {
                NetworkStream stream = client.GetStream();
                string up = Message(0, "raised");
                Send(stream, up.Substring(0, 20));
                Pump(receiver);
                Expect(!receiver.HasState, "incomplete line is buffered");
                Send(stream, up.Substring(20));
                Pump(receiver);
                Expect(receiver.HasState && receiver.State == "raised", "split message reconstructed (" + receiver.Status + ", rejected=" + receiver.RejectedMessages + ")");
                Send(stream, Message(1, "lowered").Replace("\"v\":1,", "\"v\":1,\"acknowledge\":true,"));
                Pump(receiver);
                receiver.ReportPresentation(false, false);
                stream.ReadTimeout = 1000;
                string ackLine;
                using (var ackReader = new System.IO.StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    ackLine = ackReader.ReadLine();
                var acknowledgment = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(ackLine);
                Expect((string)acknowledgment["type"] == "vision_ack" && !(bool)acknowledgment["leftVisible"]
                    && Convert.ToInt64(acknowledgment["sequence"]) == 1, "post-controller acknowledgment sent through the active connection");
                Send(stream, Message(2, "lowered") + Message(3, "raised"));
                Pump(receiver);
                Expect(receiver.State == "raised", "coalesced messages processed in order");
                Send(stream, Message(1, "lowered") + Message(4, "bad_state") + "not-json\n"
                    + Message(4, "raised").Replace("\"confidence\":0.9,", ""));
                Pump(receiver);
                Expect(receiver.State == "raised" && receiver.RejectedMessages == 4, "stale sequence, invalid messages, and missing fields rejected");
                Send(stream, Message(4, "unknown"));
                Pump(receiver);
                Expect(receiver.State == "unknown", "unknown pose accepted distinctly");
                Send(stream, Message(5, "raised"));
                Pump(receiver);
                Thread.Sleep(600);
                Call(receiver, "Update");
                Expect(!receiver.HasState, "silent connection times out");
            }
            Pump(receiver);
            Expect(!receiver.Connected && !receiver.HasState, "orderly disconnect clears state");
            using (var client = new TcpClient("127.0.0.1", port))
            {
                Send(client.GetStream(), Message(0, "lowered"));
                Pump(receiver);
                Expect(receiver.HasState && receiver.State == "lowered", "reconnection resets sequence");
                Send(client.GetStream(), new string('x', 4097));
                Pump(receiver);
                Expect(!receiver.Connected, "oversized line disconnects");
            }
        }
        finally { Call(receiver, "OnDisable"); }
        Call(receiver, "OnEnable");
        Call(receiver, "OnDisable");
        Expect(true, "port can be reused after shutdown");
    }
}
