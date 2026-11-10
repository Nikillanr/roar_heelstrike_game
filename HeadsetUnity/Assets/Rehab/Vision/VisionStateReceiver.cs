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
        public float LastQueueMilliseconds { get; private set; }

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
            public bool acknowledge = false;
        }

        [Serializable]
        private sealed class Acknowledgment
        {
            public string type = "vision_ack";
            public string sessionId;
            public long sequence;
            public bool leftVisible;
            public bool rightVisible;
            public double unityUpdateMs;
        }

        private sealed class Peer
        {
            public readonly ConcurrentQueue<byte[]> Outgoing = new ConcurrentQueue<byte[]>();
        }

        private struct Packet
        {
            public string Kind;
            public string Line;
            public double Receipt;
            public Peer Peer;
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
        private Peer activePeer;
        private bool acknowledgmentPending;

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
            LastQueueMilliseconds = -1f;
            sessionId = null;
            sequence = -1;
            activePeer = null;
            acknowledgmentPending = false;
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
                    activePeer = packet.Peer;
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
            LastQueueMilliseconds = (float)Math.Max(0d, (RehabClock.MonotonicSeconds - packet.Receipt) * 1000d);
            HasState = true;
            acknowledgmentPending = value.acknowledge;
            Status = "Receiving " + State;
        }

        // Called after all Update methods. Confirms controller state, not monitor scanout.
        public void ReportPresentation(bool leftVisible, bool rightVisible)
        {
            if (!acknowledgmentPending || activePeer == null) return;
            acknowledgmentPending = false;
            if (activePeer.Outgoing.Count >= 128) return;
            var acknowledgment = new Acknowledgment {
                sessionId = sessionId, sequence = sequence,
                leftVisible = leftVisible, rightVisible = rightVisible,
                unityUpdateMs = Math.Max(0d, (RehabClock.MonotonicSeconds - ReceivedAt) * 1000d)
            };
            activePeer.Outgoing.Enqueue(Encoding.UTF8.GetBytes(JsonUtility.ToJson(acknowledgment) + "\n"));
        }

        private static bool ValidState(string state)
        {
            return state == "raised" || state == "lowered" || state == "unknown";
        }

        private static bool ValidConfidence(float confidence)
        {
            return !float.IsNaN(confidence) && !float.IsInfinity(confidence) && confidence >= 0f && confidence <= 1f;
        }

        private static void Enqueue(Run current, string kind, string line = null, Peer peer = null)
        {
            if (current.Queue.Count >= 128) throw new IOException("Vision queue full; reconnect for fresh state.");
            current.Queue.Enqueue(new Packet { Kind = kind, Line = line, Receipt = RehabClock.MonotonicSeconds, Peer = peer });
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
                        client.SendTimeout = 250;
                        var peer = new Peer();
                        Enqueue(current, "connect", peer: peer);
                        ReadClient(current, client, peer);
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

        private static void ReadClient(Run current, TcpClient client, Peer peer)
        {
            var buffer = new byte[1024];
            var utf8 = new UTF8Encoding(false, true);
            using (var pending = new MemoryStream())
            {
                NetworkStream stream = client.GetStream();
                while (!current.Cancel.IsCancellationRequested)
                {
                    byte[] outgoing;
                    while (peer.Outgoing.TryDequeue(out outgoing)) stream.Write(outgoing, 0, outgoing.Length);
                    // Wake frequently for acknowledgments without blocking Unity's main thread.
                    if (!client.Client.Poll(1000, SelectMode.SelectRead)) continue;
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
