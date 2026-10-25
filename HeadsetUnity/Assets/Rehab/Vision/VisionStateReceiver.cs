using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisionStateReceiver : MonoBehaviour
    {
        public string bindAddress = "127.0.0.1";
        public int port = 7778;
        public float staleSeconds = 1f;

        public string Status { get; private set; }
        public string State { get; private set; }
        public string Hand { get; private set; }
        public float Confidence { get; private set; }
        public double ReceivedAt { get; private set; }
        public bool HasState { get; private set; }
        public bool Connected { get; private set; }
        public int RejectedMessages { get; private set; }
        public string LeftState { get; private set; }
        public string RightState { get; private set; }
        public float LeftConfidence { get; private set; }
        public float RightConfidence { get; private set; }
        public int ConnectionRevision { get; private set; }

        [Serializable]
        private sealed class Message
        {
            public int v = -1;
            public string type = "";
            public string sessionId = "";
            public long sequence = -1;
            public string hand = "";
            public string state = "";
            public float confidence = -1f;
            public double sourceTime = -1d;
            public string leftState = "";
            public string rightState = "";
            public float leftConfidence = -1f;
            public float rightConfidence = -1f;
        }

        private struct Packet
        {
            public string Kind;
            public string Line;
            public double Receipt;
        }

        // Each start owns its own sockets and queue, including during a rapid Play-mode restart.
        private sealed class Run
        {
            public readonly ConcurrentQueue<Packet> Queue = new ConcurrentQueue<Packet>();
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public TcpListener Listener;
            public TcpClient Client;
            public Thread Thread;
            public readonly object Gate = new object();
        }

        private Run run;
        private string sessionId;
        private long sequence = -1;

        private void OnEnable()
        {
            ClearState();
            RejectedMessages = 0;
            var next = new Run();
            try
            {
                next.Listener = new TcpListener(IPAddress.Parse(bindAddress), port);
                next.Listener.Start();
                run = next;
                Status = "Listening on " + bindAddress + ":" + port;
                next.Thread = new Thread(() => Listen(next)) { IsBackground = true, Name = "Rehab Vision Input" };
                next.Thread.Start();
            }
            catch (Exception error)
            {
                if (next.Listener != null) next.Listener.Stop();
                Status = "Cannot listen: " + error.Message;
                Debug.LogError("[Vision] " + Status, this);
            }
        }

        private void OnDisable()
        {
            Run previous = run;
            run = null;
            if (previous != null)
            {
                previous.Cancel.Cancel();
                previous.Listener.Stop();
                lock (previous.Gate) { if (previous.Client != null) previous.Client.Close(); }
                if (previous.Thread != null) previous.Thread.Join(1000);
            }
            ClearState();
            Status = "Stopped";
        }

        private void ClearState()
        {
            Connected = false;
            HasState = false;
            State = "unknown";
            LeftState = RightState = "unknown";
            LeftConfidence = RightConfidence = 0f;
            Hand = "either";
            Confidence = 0f;
            ReceivedAt = 0d;
            sessionId = null;
            sequence = -1;
        }

        private void Update()
        {
            Run current = run;
            if (current == null) return;
            Packet packet;
            while (current.Queue.TryDequeue(out packet))
            {
                if (packet.Kind == "connect")
                {
                    ClearState();
                    ConnectionRevision++;
                    Connected = true;
                    Status = "Vision connected; awaiting state";
                }
                else if (packet.Kind == "disconnect")
                {
                    ClearState();
                    Status = "Disconnected; waiting for vision";
                }
                else if (packet.Kind == "error")
                {
                    ClearState();
                    Status = "Connection error: " + packet.Line;
                }
                else if (RehabClock.MonotonicSeconds - packet.Receipt <= Mathf.Max(0.1f, staleSeconds))
                    Apply(packet);
            }
            if (HasState && RehabClock.MonotonicSeconds - ReceivedAt > Mathf.Max(0.1f, staleSeconds))
            {
                HasState = false;
                State = "unknown";
                LeftState = RightState = "unknown";
                Status = "State timed out";
            }
        }

        private void Apply(Packet packet)
        {
            // Preserve invalid sentinels for omitted fields instead of accepting zero defaults.
            Message value = new Message();
            try { JsonUtility.FromJsonOverwrite(packet.Line, value); }
            catch (Exception) { RejectedMessages++; return; }
            if (value == null || (value.v != 1 && value.v != 2) || value.type != "hand_state"
                || string.IsNullOrWhiteSpace(value.sessionId) || value.sessionId.Length > 128
                || value.sequence < 0 || (sessionId != null && value.sessionId != sessionId)
                || (sessionId == value.sessionId && value.sequence <= sequence)
                || !ValidState(value.state)
                || (value.hand != "left" && value.hand != "right" && value.hand != "either")
                || !ValidConfidence(value.confidence)
                || (value.v == 2 && (!ValidState(value.leftState) || !ValidState(value.rightState)
                    || !ValidConfidence(value.leftConfidence) || !ValidConfidence(value.rightConfidence)))
                || double.IsNaN(value.sourceTime) || double.IsInfinity(value.sourceTime) || value.sourceTime < 0d)
            {
                RejectedMessages++;
                return;
            }
            sessionId = value.sessionId;
            sequence = value.sequence;
            State = value.state;
            Hand = value.hand;
            Confidence = value.confidence;
            // Version 1 cannot distinguish hands in "either" mode; preserve it as a both-hands control.
            LeftState = value.v == 2 ? value.leftState : value.hand == "right" ? "lowered" : value.state;
            RightState = value.v == 2 ? value.rightState : value.hand == "left" ? "lowered" : value.state;
            LeftConfidence = value.v == 2 ? value.leftConfidence : value.confidence;
            RightConfidence = value.v == 2 ? value.rightConfidence : value.confidence;
            ReceivedAt = packet.Receipt;
            HasState = true;
            Status = "Receiving " + State;
        }

        private static bool ValidState(string state)
        {
            return state == "raised" || state == "lowered" || state == "unknown";
        }

        private static bool ValidConfidence(float confidence)
        {
            return !float.IsNaN(confidence) && !float.IsInfinity(confidence) && confidence >= 0f && confidence <= 1f;
        }

        private static void Enqueue(Run current, string kind, string line = null)
        {
            if (current.Queue.Count >= 128) throw new IOException("Vision queue full; reconnect for fresh state.");
            current.Queue.Enqueue(new Packet { Kind = kind, Line = line, Receipt = RehabClock.MonotonicSeconds });
        }

        private static void Listen(Run current)
        {
            try
            {
                while (!current.Cancel.IsCancellationRequested)
                {
                    TcpClient client = current.Listener.AcceptTcpClient();
                    lock (current.Gate)
                    {
                        if (current.Cancel.IsCancellationRequested) { client.Close(); break; }
                        current.Client = client;
                    }
                    try
                    {
                        client.NoDelay = true;
                        Enqueue(current, "connect");
                        ReadClient(current, client);
                    }
                    catch (Exception error)
                    {
                        if (!current.Cancel.IsCancellationRequested && current.Queue.Count < 127)
                            Enqueue(current, "error", error.Message);
                    }
                    finally
                    {
                        client.Close();
                        lock (current.Gate) { current.Client = null; }
                        // Discard overflow backlog instead of allowing it to restore old raised states.
                        if (current.Queue.Count >= 127) { Packet ignored; while (current.Queue.TryDequeue(out ignored)) { } }
                        Enqueue(current, "disconnect");
                    }
                }
            }
            catch (Exception error)
            {
                if (!current.Cancel.IsCancellationRequested && current.Queue.Count < 128)
                    Enqueue(current, "error", error.Message);
            }
        }

        private static void ReadClient(Run current, TcpClient client)
        {
            var buffer = new byte[1024];
            var utf8 = new UTF8Encoding(false, true);
            using (var pending = new MemoryStream())
            {
                NetworkStream stream = client.GetStream();
                while (!current.Cancel.IsCancellationRequested)
                {
                    int count = stream.Read(buffer, 0, buffer.Length);
                    if (count == 0) return;
                    for (int i = 0; i < count; i++)
                    {
                        if (buffer[i] == 10)
                        {
                            if (pending.Length > 0) Enqueue(current, "state", utf8.GetString(pending.ToArray()).TrimEnd('\r'));
                            pending.SetLength(0);
                        }
                        else
                        {
                            if (pending.Length >= 4096) throw new IOException("Vision message exceeds 4096 bytes.");
                            pending.WriteByte(buffer[i]);
                        }
                    }
                }
            }
        }
    }
}
