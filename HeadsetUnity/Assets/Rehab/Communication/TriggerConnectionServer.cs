using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class TriggerConnectionServer : MonoBehaviour
    {
        [Header("Trigger connection")]
        public int port = 7777;
        public bool listenOnEnable = true;

        [Header("Live status")]
        [SerializeField] private bool listening;
        [SerializeField] private bool clientConnected;
        [SerializeField] private string status = "Stopped";
        [SerializeField] private string lastLine = "";

        private struct ReceivedPacket
        {
            public string Line;
            public double ReceiptTime;
        }

        private readonly ConcurrentQueue<ReceivedPacket> incoming = new ConcurrentQueue<ReceivedPacket>();
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private Thread worker;
        private TcpListener listener;
        private volatile bool workerRunning;
        private volatile bool workerListening;
        private volatile bool workerConnected;
        private volatile string workerStatus = "Stopped";

        public event Action<HeelStrikeEvent, double> HeelStrikeReceived;
        public event Action<string> LineReceived;

        public bool Listening { get { return listening; } }
        public bool ClientConnected { get { return clientConnected; } }
        public string Status { get { return status; } }
        public string LastLine { get { return lastLine; } }

        private void OnEnable()
        {
            if (listenOnEnable) StartServer();
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void Update()
        {
            listening = workerListening;
            clientConnected = workerConnected;
            status = workerStatus;
            ReceivedPacket packet;
            while (incoming.TryDequeue(out packet)) ProcessPacket(packet);
        }

        [ContextMenu("Start Trigger Server")]
        public void StartServer()
        {
            if (workerRunning) return;
            workerRunning = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "mTPAD Trigger Server" };
            worker.Start();
        }

        [ContextMenu("Stop Trigger Server")]
        public void StopServer()
        {
            workerRunning = false;
            try { if (listener != null) listener.Stop(); }
            catch { }
            if (worker != null && worker.IsAlive) worker.Join(750);
            worker = null;
            workerListening = false;
            workerConnected = false;
            workerStatus = "Stopped";
        }

        public bool SendAck(HeelStrikeAck ack)
        {
            return SendLine(RehabProtocol.EncodeAck(ack));
        }

        public bool SendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !workerConnected) return false;
            outgoing.Enqueue(line.Trim());
            return true;
        }

        private void WorkerLoop()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                workerListening = true;
                workerStatus = "Listening on port " + port;
                while (workerRunning)
                {
                    TcpClient client = null;
                    try
                    {
                        client = listener.AcceptTcpClient();
                        client.NoDelay = true;
                        workerConnected = true;
                        workerStatus = "Trigger connected";
                        RunClient(client);
                    }
                    catch (SocketException)
                    {
                        if (workerRunning) workerStatus = "Socket error; waiting";
                    }
                    catch (Exception exception)
                    {
                        if (workerRunning) workerStatus = "Client error: " + exception.Message;
                    }
                    finally
                    {
                        workerConnected = false;
                        if (client != null) client.Close();
                        if (workerRunning) workerStatus = "Listening on port " + port;
                    }
                }
            }
            catch (Exception exception)
            {
                workerStatus = "Server failed: " + exception.Message;
            }
            finally
            {
                workerListening = false;
                workerConnected = false;
                try { if (listener != null) listener.Stop(); }
                catch { }
                listener = null;
            }
        }

        private void RunClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            var pending = new StringBuilder();
            var buffer = new byte[4096];
            while (workerRunning && client.Connected)
            {
                string send;
                while (outgoing.TryDequeue(out send))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(send + "\n");
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
                if (line.Length > 0)
                {
                    incoming.Enqueue(new ReceivedPacket
                    {
                        Line = line,
                        ReceiptTime = RehabClock.MonotonicSeconds
                    });
                }
            }
        }

        private void ProcessPacket(ReceivedPacket packet)
        {
            lastLine = packet.Line;
            string command;
            Dictionary<string, string> fields;
            if (RehabProtocol.TryParse(packet.Line, out command, out fields)
                && command == RehabProtocol.SyncRequestCommand)
            {
                long sequence = RehabProtocol.Long(fields, "seq");
                double pcSend = RehabProtocol.Double(fields, "pcSend");
                double headsetSend = RehabClock.MonotonicSeconds;
                SendLine(RehabProtocol.EncodeSyncResponse(sequence, pcSend, packet.ReceiptTime, headsetSend));
                return;
            }

            HeelStrikeEvent heelStrike;
            string error;
            if (RehabProtocol.TryDecodeHeelStrike(packet.Line, out heelStrike, out error))
            {
                Action<HeelStrikeEvent, double> heelHandler = HeelStrikeReceived;
                if (heelHandler != null) heelHandler(heelStrike, packet.ReceiptTime);
            }

            Action<string> lineHandler = LineReceived;
            if (lineHandler != null) lineHandler(packet.Line);
        }

        private static int IndexOf(StringBuilder builder, char value)
        {
            for (int i = 0; i < builder.Length; i++) if (builder[i] == value) return i;
            return -1;
        }
    }
}
