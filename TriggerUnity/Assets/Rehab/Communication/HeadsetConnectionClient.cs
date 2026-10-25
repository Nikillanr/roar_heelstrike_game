using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class HeadsetConnectionClient : MonoBehaviour
    {
        [Header("Headset connection")]
        public string headsetAddress = "192.168.1.2";
        public int port = 7777;
        public bool connectOnEnable = true;
        [Range(0.25f, 10f)] public float reconnectDelaySeconds = 2f;
        [Range(0.25f, 10f)] public float syncIntervalSeconds = 2f;

        [Header("Live status")]
        [SerializeField] private bool connected;
        [SerializeField] private string status = "Stopped";
        [SerializeField] private float lastRoundTripMilliseconds;
        [SerializeField] private double headsetClockOffsetSeconds;

        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private Thread worker;
        private volatile bool workerRunning;
        private volatile bool workerConnected;
        private volatile string workerStatus = "Stopped";
        private long syncSequence;
        private float nextSyncTime;

        public event Action<string> LineReceived;
        public event Action<HeelStrikeAck> AckReceived;

        public bool Connected { get { return connected; } }
        public string Status { get { return status; } }
        public float LastRoundTripMilliseconds { get { return lastRoundTripMilliseconds; } }
        public double HeadsetClockOffsetSeconds { get { return headsetClockOffsetSeconds; } }

        private void OnEnable()
        {
            if (connectOnEnable) StartClient();
        }

        private void OnDisable()
        {
            StopClient();
        }

        private void Update()
        {
            connected = workerConnected;
            status = workerStatus;
            string line;
            while (incoming.TryDequeue(out line))
            {
                ProcessIncoming(line);
                Action<string> handler = LineReceived;
                if (handler != null) handler(line);
            }

            if (connected && Time.unscaledTime >= nextSyncTime)
            {
                nextSyncTime = Time.unscaledTime + syncIntervalSeconds;
                syncSequence++;
                double now = RehabClock.MonotonicSeconds;
                outgoing.Enqueue(RehabProtocol.EncodeSyncRequest(syncSequence, now));
            }
        }

        [ContextMenu("Connect to Headset")]
        public void StartClient()
        {
            if (workerRunning) return;
            workerRunning = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "mTPAD Headset Client" };
            worker.Start();
            workerStatus = "Connecting";
        }

        [ContextMenu("Disconnect from Headset")]
        public void StopClient()
        {
            workerRunning = false;
            if (worker != null && worker.IsAlive) worker.Join(750);
            worker = null;
            workerConnected = false;
            connected = false;
            workerStatus = "Stopped";
        }

        public bool SendHeelStrike(HeelStrikeEvent heelStrike)
        {
            if (heelStrike == null || !workerConnected) return false;
            heelStrike.sentPcTime = RehabClock.MonotonicSeconds;
            outgoing.Enqueue(RehabProtocol.EncodeHeelStrike(heelStrike));
            return true;
        }

        public bool SendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !workerConnected) return false;
            outgoing.Enqueue(line.Trim());
            return true;
        }

        private void WorkerLoop()
        {
            while (workerRunning)
            {
                TcpClient client = null;
                try
                {
                    workerStatus = "Connecting to " + headsetAddress + ":" + port;
                    client = new TcpClient { NoDelay = true };
                    IAsyncResult connect = client.BeginConnect(headsetAddress, port, null, null);
                    bool completed = connect.AsyncWaitHandle.WaitOne(2000);
                    connect.AsyncWaitHandle.Close();
                    if (!completed)
                    {
                        client.Close();
                        throw new TimeoutException("Connection timed out");
                    }
                    client.EndConnect(connect);
                    workerConnected = true;
                    workerStatus = "Connected";
                    RunConnected(client);
                }
                catch (Exception exception)
                {
                    workerStatus = "Disconnected: " + exception.Message;
                }
                finally
                {
                    workerConnected = false;
                    if (client != null) client.Close();
                }

                int delay = Mathf.Max(250, Mathf.RoundToInt(reconnectDelaySeconds * 1000f));
                int waited = 0;
                while (workerRunning && waited < delay)
                {
                    Thread.Sleep(50);
                    waited += 50;
                }
            }
        }

        private void RunConnected(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            var pending = new StringBuilder();
            var buffer = new byte[4096];
            while (workerRunning && client.Connected)
            {
                string line;
                while (outgoing.TryDequeue(out line))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                }

                while (stream.DataAvailable)
                {
                    int count = stream.Read(buffer, 0, buffer.Length);
                    if (count <= 0) throw new SocketException();
                    pending.Append(Encoding.UTF8.GetString(buffer, 0, count));
                    ExtractLines(pending);
                }
                Thread.Sleep(5);
            }
        }

        private void ExtractLines(StringBuilder pending)
        {
            while (true)
            {
                int newline = IndexOf(pending, '\n');
                if (newline < 0) return;
                string line = pending.ToString(0, newline).TrimEnd('\r');
                pending.Remove(0, newline + 1);
                if (line.Length > 0) incoming.Enqueue(line);
            }
        }

        private void ProcessIncoming(string line)
        {
            HeelStrikeAck ack;
            if (RehabProtocol.TryDecodeAck(line, out ack))
            {
                Action<HeelStrikeAck> handler = AckReceived;
                if (handler != null) handler(ack);
                return;
            }

            string command;
            Dictionary<string, string> fields;
            if (!RehabProtocol.TryParse(line, out command, out fields)
                || command != RehabProtocol.SyncResponseCommand) return;
            double pcSend = RehabProtocol.Double(fields, "pcSend");
            double headsetReceipt = RehabProtocol.Double(fields, "headsetReceipt");
            double headsetSend = RehabProtocol.Double(fields, "headsetSend");
            double pcReceive = RehabClock.MonotonicSeconds;
            double networkRtt = (pcReceive - pcSend) - Math.Max(0d, headsetSend - headsetReceipt);
            lastRoundTripMilliseconds = (float)(Math.Max(0d, networkRtt) * 1000d);
            headsetClockOffsetSeconds = ((headsetReceipt + headsetSend) * 0.5d)
                - ((pcSend + pcReceive) * 0.5d);
        }

        private static int IndexOf(StringBuilder builder, char value)
        {
            for (int i = 0; i < builder.Length; i++) if (builder[i] == value) return i;
            return -1;
        }
    }
}
