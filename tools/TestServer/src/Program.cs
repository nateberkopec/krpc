using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using KRPC;
using KRPC.Server;
using KRPC.Server.TCP;
using KRPC.Service;
using KRPC.Utils;
using NDesk.Options;

namespace TestServer
{
    [SuppressMessage ("Gendarme.Rules.Correctness", "DeclareEventsExplicitlyRule")]
    static class MainClass
    {
        static void Help (OptionSet options)
        {
            Console.WriteLine ("usage: TestServer.exe [-h] [-v] [--bind=address] [--rpc_port=VALUE] [--stream_port=VALUE]");
            Console.WriteLine ("                      [--type=TYPE] [--debug] [--quiet] [--server-debug]");
            Console.WriteLine ();
            Console.WriteLine ("A kRPC test server for the client library unit tests");
            Console.WriteLine ();
            options.WriteOptionDescriptions (Console.Out);
        }

        [SuppressMessage ("Gendarme.Rules.Smells", "AvoidLongMethodsRule")]
        public static void Main (string[] args)
        {
            bool showHelp = false;
            bool showVersion = false;

            Logger.Format = "{0:G} - {1} - {2}";
            Logger.Enabled = true;
            Logger.Level = Logger.Severity.Info;
            bool serverDebug = false;
            string bind = "127.0.0.1";
            ushort rpcPort = 0;
            ushort streamPort = 0;
            string type = "protobuf";

            var options = new OptionSet { {
                    "h|help", "show this help message and exit",
                    v => showHelp = v != null
                }, {
                    "v|version", "show program's version number and exit",
                    v => showVersion = v != null
                }, {
                    "bind=", "Address to bind the server to. If unspecified, the loopback address is used (127.0.0.1).",
                    v => bind = v
                }, {
                    "rpc-port=", "Port number to use for the RPC server. If unspecified, use an ephemeral port.",
                    (ushort v) => rpcPort = v
                }, {
                    "stream-port=", "Port number to use for the stream server. If unspecified, use an ephemeral port.",
                    (ushort v) => streamPort = v
                }, {
                    "type=", "Type of server to run. Either protobuf, websockets or websockets-echo.",
                    v => type = v
                }, {
                    "debug", "Set log level to 'debug', defaults to 'info'",
                    v => {
                        if (v != null) {
                            Logger.Enabled = true;
                            Logger.Level = Logger.Severity.Debug;
                        }
                    }
                }, { "quiet", "Set log level to 'warning'",
                    v => {
                        if (v != null) {
                            Logger.Enabled = true;
                            Logger.Level = Logger.Severity.Warning;
                        }
                    }
                }, {
                    "server-debug", "Output debug information about the server",
                    v => serverDebug = v != null
                }
            };
            options.Parse (args);

            if (showHelp) {
                Help (options);
                return;
            }

            if (showVersion) {
                var assembly = Assembly.GetEntryAssembly ();
                var info = FileVersionInfo.GetVersionInfo (assembly.Location);
                var version = string.Format ("{0}.{1}.{2}", info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
                Console.WriteLine ("TestServer.exe version " + version);
                return;
            }

            KRPC.Service.Scanner.Scanner.GetServices ();

            var core = Core.Instance;
            CallContext.SetGameScene (GameScene.SpaceCenter);
            core.OnClientRequestingConnection += (s, e) => e.Request.Allow ();

            IPAddress bindAddress;
            if (!IPAddress.TryParse(bind, out bindAddress)) {
                Console.WriteLine("Failed to parse bind address.");
                return;
            }

            var rpcTcpServer = new TCPServer (bindAddress, rpcPort);
            var streamTcpServer = new TCPServer (bindAddress, streamPort);

            Server server;
            if (type == "protobuf") {
                var rpcServer = new KRPC.Server.ProtocolBuffers.RPCServer (rpcTcpServer);
                var streamServer = new KRPC.Server.ProtocolBuffers.StreamServer (streamTcpServer);
                server = new Server (Guid.NewGuid (), Protocol.ProtocolBuffersOverTCP, "TestServer", rpcServer, streamServer);
            } else if (type == "websockets") {
                var rpcServer = new KRPC.Server.WebSockets.RPCServer (rpcTcpServer);
                var streamServer = new KRPC.Server.WebSockets.StreamServer (streamTcpServer);
                server = new Server (Guid.NewGuid (), Protocol.ProtocolBuffersOverWebsockets, "TestServer", rpcServer, streamServer);
            } else if (type == "websockets-echo") {
                var rpcServer = new KRPC.Server.WebSockets.RPCServer (rpcTcpServer, true);
                var streamServer = new KRPC.Server.WebSockets.StreamServer (streamTcpServer);
                server = new Server (Guid.NewGuid (), Protocol.ProtocolBuffersOverWebsockets, "TestServer", rpcServer, streamServer);
            } else {
                Logger.WriteLine ("Server type '" + type + "' not supported", Logger.Severity.Error);
                return;
            }
            core.Add (server);

            Logger.WriteLine ("Starting server...");
            core.StartAll ();
            Logger.WriteLine ("type = " + type);
            Logger.WriteLine ("bind = " + bindAddress);
            Logger.WriteLine ("rpc_port = " + rpcTcpServer.ActualPort);
            Logger.WriteLine ("stream_port = " + streamTcpServer.ActualPort);
            Logger.WriteLine ("Server started successfully");

            const long targetFPS = 60;
            long update = 0;
            long ticksPerUpdate = Stopwatch.Frequency / targetFPS;
            var timer = new Stopwatch ();
            while (server.Running) {
                timer.Reset ();
                timer.Start ();

                core.Update ();

                if (serverDebug && update % targetFPS == 0) {
                    // Output details about whether server.Update() took too little or too long to execute
                    var elapsed = timer.ElapsedTicks;
                    var diffTicks = Math.Abs (ticksPerUpdate - elapsed);
                    var diffTime = Math.Round (diffTicks / (double)Stopwatch.Frequency * 1000d, 2);
                    if (elapsed > ticksPerUpdate)
                        Console.WriteLine ("Slow by " + diffTime + " ms (" + diffTicks + " ticks)");
                    else
                        Console.WriteLine ("Fast by " + diffTime + " ms (" + diffTicks + " ticks)");
                }

                // Wait, to force 60 FPS
                while (timer.ElapsedTicks < ticksPerUpdate) {
                }
                update++;
            }
        }
    }
}
