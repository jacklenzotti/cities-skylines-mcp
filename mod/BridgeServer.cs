using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CS1McpBridge
{
    /// <summary>
    /// Loopback TCP server speaking newline-delimited JSON (see PROTOCOL.md).
    /// One JSON request per line in, one JSON response per line out. Multiple
    /// concurrent clients are allowed; each gets its own worker thread.
    /// </summary>
    public static class BridgeServer
    {
        const int DefaultPort = 50545;          // override with env CS1MCP_PORT
        static TcpListener _listener;
        static Thread _accept;
        static volatile bool _running;

        public static void Start()
        {
            if (_running) return;

            int port = DefaultPort;
            var env = Environment.GetEnvironmentVariable("CS1MCP_PORT");
            if (!string.IsNullOrEmpty(env)) int.TryParse(env, out port);

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            catch (Exception e)
            {
                Log.Error("failed to bind 127.0.0.1:" + port + " — " + e.Message);
                return;
            }

            _running = true;
            _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "CS1McpBridge.Accept" };
            _accept.Start();
            Log.Info("listening on 127.0.0.1:" + port);
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            Log.Info("stopped");
        }

        static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_running) continue; else break; }

                var worker = new Thread(() => Serve(client)) { IsBackground = true };
                worker.Start();
            }
        }

        static void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    string response = Commands.Handle(line);
                    writer.WriteLine(response);
                }
            }
        }
    }
}
