using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Keeper244.Net
{
    /// <summary>
    /// Receives UDP datagrams on a background thread and queues them, with their arrival time, for
    /// the main thread. Plain .NET (no UnityEngine), so it can also be tested outside Unity.
    ///
    /// Binds to 127.0.0.1 by default: the bridge runs on the same PC and Windows shows no firewall
    /// prompt. Pass IPAddress.Any to accept a bridge on another computer.
    /// </summary>
    public sealed class UdpDatagramListener : IDisposable
    {
        public struct Datagram
        {
            public string Text;
            /// <summary>Arrival time on <see cref="Now"/>'s clock, seconds.</summary>
            public double Time;
        }

        /// <summary>If the main thread stalls, the oldest datagrams beyond this are dropped.</summary>
        public const int MaxQueued = 2000;

        readonly ConcurrentQueue<Datagram> queue = new ConcurrentQueue<Datagram>();
        readonly IPAddress address;
        readonly int port;
        UdpClient client;
        Thread thread;
        volatile bool running;
        long received;

        /// <summary>Monotonic high-resolution clock shared by the listener and its users, seconds.</summary>
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public UdpDatagramListener(IPAddress address, int port)
        {
            this.address = address ?? IPAddress.Loopback;
            this.port = port;
        }

        /// <summary>Why listening failed (e.g. the port is in use), or null.</summary>
        public string Error { get; private set; }
        public bool IsListening => running;
        public long Received => Interlocked.Read(ref received);

        /// <summary>The bound port (useful when constructed with port 0).</summary>
        public int LocalPort => client?.Client?.LocalEndPoint is IPEndPoint ep ? ep.Port : port;

        public bool Start()
        {
            if (running) return true;
            try
            {
                client = new UdpClient(new IPEndPoint(address, port));
                UdpSockets.IgnoreConnectionResets(client);
            }
            catch (SocketException e)
            {
                Error = $"cannot listen on {address}:{port} ({e.SocketErrorCode}). Is another copy of the game running?";
                client = null;
                return false;
            }

            Error = null;
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = $"Keeper244 UDP :{port}" };
            thread.Start();
            return true;
        }

        void Loop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref from);
                    double t = Now;
                    queue.Enqueue(new Datagram { Text = Encoding.UTF8.GetString(data), Time = t });
                    Interlocked.Increment(ref received);
                    while (queue.Count > MaxQueued && queue.TryDequeue(out _)) { }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!running) break;
                    Thread.Sleep(5); // transient (e.g. Windows ICMP reset); keep listening
                }
            }
        }

        public bool TryDequeue(out Datagram datagram) => queue.TryDequeue(out datagram);

        public void Dispose()
        {
            running = false;
            try { client?.Close(); } catch (SocketException) { }
            if (thread != null && thread != Thread.CurrentThread) thread.Join(500);
            thread = null;
            client = null;
        }
    }

    /// <summary>Fire-and-forget UDP sender (one JSON message per datagram). Never throws on send.</summary>
    public sealed class UdpDatagramSender : IDisposable
    {
        readonly UdpClient client;
        readonly IPEndPoint target;

        public UdpDatagramSender(string host, int port)
        {
            target = new IPEndPoint(IPAddress.Parse(host), port);
            client = new UdpClient(AddressFamily.InterNetwork);
            UdpSockets.IgnoreConnectionResets(client);
        }

        public long Sent { get; private set; }
        public long Failed { get; private set; }
        public string LastError { get; private set; }
        public string Target => target.ToString();

        public bool Send(string text)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                client.Send(bytes, bytes.Length, target);
                Sent++;
                return true;
            }
            catch (SocketException e)
            {
                Failed++;
                LastError = e.SocketErrorCode.ToString();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            try { client.Close(); } catch (SocketException) { }
        }
    }

    static class UdpSockets
    {
        /// <summary>
        /// Windows reports "port unreachable" from an earlier send as an error on the NEXT socket call
        /// (WSAECONNRESET), e.g. when the game sends events before Python listens. Turn that off.
        /// </summary>
        public static void IgnoreConnectionResets(UdpClient client)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                client.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception) { /* not supported on this runtime: harmless */ }
        }
    }
}
