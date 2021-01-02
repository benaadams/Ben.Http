using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using Tmds.Linux;
using static Tmds.Linux.LibC;

namespace Ben.Http
{
    public class HttpServer : IDisposable
    {
        private IServerAddressesFeature _addresses = null!;
        private TaskCompletionSource _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private ILoggerFactory _loggerFactory;
        private IServer _server;
        private KestrelOptions _kestrelOptions;
        private SocketOptions _socketOptions;
        private bool _webGarden;
        private List<SafeSocketHandle>? _managedSockets = null;

        public IFeatureCollection Features => _server.Features;

        public HttpServer(string listenAddress) : this(DefaultLoggerFactories.Empty)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            _addresses.Addresses.Add(listenAddress);
        }

        [SupportedOSPlatform("linux")]
        public HttpServer(string listenAddress, bool webGarden) : this(DefaultLoggerFactories.Empty, webGarden)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            _addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(string listenAddress, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            _addresses.Addresses.Add(listenAddress);
        }

        [SupportedOSPlatform("linux")]
        public HttpServer(string listenAddress, ILoggerFactory loggerFactory, bool webGarden) : this(loggerFactory, webGarden)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            _addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(IEnumerable<string> listenAddresses, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            foreach (var uri in listenAddresses)
            {
                _addresses.Addresses.Add(uri);
            };
        }

        [SupportedOSPlatform("linux")]
        public HttpServer(IEnumerable<string> listenAddresses, ILoggerFactory loggerFactory, bool webGarden) : this(loggerFactory, webGarden)
        {
            _addresses = Features.Get<IServerAddressesFeature>();
            foreach (var uri in listenAddresses)
            {
                _addresses.Addresses.Add(uri);
            };
        }

        private HttpServer(ILoggerFactory loggerFactory, bool webGarden = false)
        {
            _webGarden = webGarden;

            _loggerFactory = loggerFactory;
            if (!webGarden)
            {
                _kestrelOptions = KestrelOptions.Defaults;
                _socketOptions = SocketOptions.Defaults;
            }
            else
            {
                _kestrelOptions = new KestrelOptions();
                _socketOptions = new SocketOptions
                {
                    Value = new SocketTransportOptions()
                    {
                        WaitForDataBeforeAllocatingBuffer = false //,
                        //UnsafePreferInlineScheduling = true,
                        //IOQueueCount = 1
                    }
                };
            }

            _server = new KestrelServer(
                _kestrelOptions,
                new SocketTransportFactory(_socketOptions, _loggerFactory),
                _loggerFactory);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Listening on:");
            foreach (var address in _addresses.Addresses)
            {
                sb.AppendLine($"=> {address}");
            }

            return sb.ToString();
        }

        private unsafe void SetupGarden(out int parentId)
        {
            var endpoints = _addresses.Addresses.Select(address => {
                var parsedAddress = BindingAddress.Parse(address);
                var https = false;

                if (parsedAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    https = true;
                }
                else if (!parsedAddress.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"UnsupportedAddressScheme {address}");
                }

                if (!IPAddress.TryParse(parsedAddress.Host, out var ip))
                {
                    return new IPEndPoint(IPAddress.Any, parsedAddress.Port);
                }
                //(IPEndPoint EndPoint, int Port, bool IsLocalHost) options;
                //if (parsedAddress.IsUnixPipe)
                //{
                //    throw new InvalidOperationException($"Unsupported Unix Pipe");
                //}
                //else if (string.Equals(parsedAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                //{
                //    // "localhost" for both IPv4 and IPv6 can't be represented as an IPEndPoint.
                //    options = (Port: parsedAddress.Port, IsLocalhost: true);
                //}
                //else if (TryCreateIPEndPoint(parsedAddress, out var endpoint))
                //{
                //    options = new ListenOptions(endpoint);
                //}
                //else
                //{
                //    // when address is 'http://hostname:port', 'http://*:port', or 'http://+:port'
                //    options = new AnyIPListenOptions(parsedAddress.Port);
                //}

                //// Kestrel expects IPv6Any to bind to both IPv6 and IPv4
                //if (ip.Address == IPAddress.IPv6Any)
                //{
                //    listenSocket.DualMode = true;
                //}
                return new IPEndPoint(ip.Address, parsedAddress.Port);
            }).ToArray();

            _addresses.Addresses.Clear();

            parentId = ForkWorkers();

            foreach (var endpoint in endpoints)
            {
                var sk = socket(AF_INET, SOCK_STREAM, 0);
                var socketHandle = new SafeSocketHandle((IntPtr)sk, ownsHandle: true);
                if (sk < 0)
                {
                    throw new InvalidOperationException();
                }

                int rv;
                int optval = 1;
                rv = setsockopt(sk, SOL_SOCKET, SO_REUSEADDR, &optval, sizeof(int));
                if (rv != 0)
                {
                    throw new SocketException(rv);
                }

                rv = setsockopt(sk, SOL_SOCKET, SO_REUSEPORT, &optval, sizeof(int));
                if (rv != 0)
                {
                    throw new SocketException(rv);
                }

                rv = setsockopt(sk, IPPROTO_TCP, TCP_NODELAY, &optval, sizeof(int));
                if (rv != 0)
                {
                    throw new SocketException(rv);
                }

                var ip = (int)endpoint.Address.Address;

                sockaddr_in addr = default;
                addr.sin_family = AF_INET;
                if (endpoint.Address != IPAddress.Any)
                {
                    *((int*)addr.sin_addr.s_addr) = ip;
                }
                addr.sin_port = htons((ushort)endpoint.Port);

                rv = bind(sk, (sockaddr*)&addr, sizeof(sockaddr));
                if (rv < 0) {
                    throw new SocketException(rv);
                }


                //var code = new SocketFilter[] {
                //new (BpfFilter.BPF_LD | BpfFilter.BPF_W | BpfFilter.BPF_ABS, 0, 0, (uint)SkfFilter.SKF_AD_OFF + (uint)SkfFilter.SKF_AD_CPU),
                //new (BpfFilter.BPF_RET | BpfFilter.BPF_A, 0, 0, 0) };

                //fixed (SocketFilter* filter = &code[0])
                //{
                //    SocketFilterProgram prog = new((ushort)code.Length, filter);
                //    rv = setsockopt(sk, SOL_SOCKET, SO_ATTACH_REUSEPORT_CBPF, &prog, sizeof(SocketFilterProgram));
                //    if (rv != 0)
                //    {
                //        throw new SocketException(rv);
                //    }
                //}

                //var listenSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //listenSocket.SetReusePort();
                //listenSocket.SetReusePortCbpf();
                //listenSocket.Bind(endpoint);

                (_managedSockets ??= new()).Add(socketHandle);

                _kestrelOptions.Value.ListenHandle((ulong)sk);
            }
        }

        public async Task RunAsync(HttpApp application, CancellationToken cancellationToken = default)
        {
            var parentId = 0;
            try
            {
                if (_webGarden)
                {
                    SetupGarden(out parentId);
                }

                Console.WriteLine($"Process {Environment.ProcessId} binding");

                await _server.StartAsync(application, cancellationToken);

                cancellationToken.UnsafeRegister(static (o) => ((HttpServer)o!)._completion.TrySetResult(), this);

                Console.WriteLine($"Process {Environment.ProcessId} listening");

                if (_webGarden && parentId > 0)
                {
                    Console.WriteLine($"Process {Environment.ProcessId} signaling parent");
                    ulong eventfd_value = 1;
                    unsafe
                    {
                        write(parentId, &eventfd_value, sizeof(ulong));
                    }
                    close(parentId);
                    parentId = 0;

                    Console.WriteLine($"Process {Environment.ProcessId} signaled parent");
                }

                await _completion.Task;

                await _server.StopAsync(default);
            }
            finally
            {
                if (_webGarden && parentId > 0)
                {
                    Console.WriteLine($"Failed process {Environment.ProcessId} signaling parent");
                    ulong eventfd_value = 1;
                    unsafe
                    {
                        write(parentId, &eventfd_value, sizeof(ulong));
                    }
                    close(parentId);
                }

                if (_managedSockets is not null)
                {
                    foreach (var socket in _managedSockets)
                    {
                        try
                        {
                            socket.Close();

                        }
                        catch (Exception) { }
                    }
                }
            }
        }

        private unsafe int ForkWorkers()
        {
            int e, efd, worker_count = 0;
            pid_t pid;
            ulong eventfd_value;
            cpu_set_t online_cpus, cpu;

            sigignore(SIGPIPE);

            // Get set/count of all online CPUs
            CPU_ZERO(&online_cpus);
            sched_getaffinity(0, sizeof(cpu_set_t), &online_cpus);
            int num_online_cpus = CPU_COUNT(&online_cpus);

            // Create a mapping between the relative cpu id and absolute cpu id for cases where the cpu ids are not contiguous
            // E.g if only cpus 0, 1, 8, and 9 are visible to the app because taskset was used or because some cpus are offline
            // then the mapping is 0 -> 0, 1 -> 1, 2 -> 8, 3 -> 9
            int[] rel_to_abs_cpu = new int[num_online_cpus];
            int rel_cpu_index = 0;

            for (int abs_cpu_index = 0; abs_cpu_index < CPU_SETSIZE; abs_cpu_index++)
            {
                if (CPU_ISSET(abs_cpu_index, &online_cpus))
                {
                    rel_to_abs_cpu[rel_cpu_index] = abs_cpu_index;
                    rel_cpu_index++;

                    if (rel_cpu_index == num_online_cpus)
                        break;
                }
            }

            List<Task> processes = new();
            // fork a new child/worker process for each available cpu
            for (int i = 0; i < rel_to_abs_cpu.Length; i++)
            {
                // Create an eventfd to communicate with the forked child process on each iteration
                // This ensures that the order of forking is deterministic which is important when using SO_ATTACH_REUSEPORT_CBPF
                efd = eventfd(0, EFD_SEMAPHORE);
                if (efd == -1)
                    throw new InvalidOperationException("eventfd");

                pid = fork();
                if (pid == -1)
                    throw new InvalidOperationException("fork");

                // Parent process. Block the for loop until the child has set cpu affinity AND started listening on its socket
                if (pid > 0)
                {
                    // Block waiting for the child process to update the eventfd semaphore as a signal to proceed
                    read(efd, &eventfd_value, sizeof(ulong));
                    close(efd);

                    processes.Add(Process.GetProcessById(pid).WaitForExitAsync());

                    worker_count++;
                    Console.WriteLine($"Worker running on CPU {i}");
                    continue;
                }

                // Child process. Set cpu affinity and return eventfd
                if (pid == 0)
                {
                    Console.WriteLine($"Worker process {Environment.ProcessId} started.");
                    CPU_ZERO(&cpu);
                    CPU_SET(rel_to_abs_cpu[i], &cpu);
                    //e = sched_setaffinity(0, sizeof (cpu_set_t), &cpu);
                    //if (e == -1)
                    //    throw new InvalidOperationException("sched_setaffinity");

                    // Break out of the for loop and continue running main. The child will signal the parent once the socket is open
                    return efd;
                }
            }

            Console.WriteLine($"Running with {worker_count} worker processes");

            Task.WaitAny(processes.ToArray()); // wait for children to exit
            throw new InvalidOperationException("A worker process has exited unexpectedly. Shutting down.");
        }

        void IDisposable.Dispose() => _server.Dispose();

        private class DefaultLoggerFactories
        {
            public static ILoggerFactory Empty => new LoggerFactory();
        }

        private class KestrelOptions : IOptions<KestrelServerOptions>
        {
            public KestrelOptions()
            {
                Value = new KestrelServerOptions();
            }

            public static KestrelOptions Defaults { get; } = new KestrelOptions();

            public KestrelServerOptions Value { get; init; }
        }

        private class SocketOptions : IOptions<SocketTransportOptions>
        {
            public static SocketOptions Defaults { get; } = new SocketOptions 
            {
                Value = new SocketTransportOptions()
                {
                    WaitForDataBeforeAllocatingBuffer = false,
                    UnsafePreferInlineScheduling = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1" : false,
                } 
            };

            public SocketTransportOptions Value { get; init; } = new SocketTransportOptions();
        }

        private class LoggerOptions : IOptionsMonitor<ConsoleLoggerOptions>
        {
            public static LoggerOptions Default { get; } = new LoggerOptions();

            public ConsoleLoggerOptions CurrentValue { get; } = new ConsoleLoggerOptions();

            public ConsoleLoggerOptions Get(string name) => CurrentValue;

            public IDisposable OnChange(Action<ConsoleLoggerOptions, string> listener)
                => NullDisposable.Shared;

            private class NullDisposable : IDisposable
            {
                public static NullDisposable Shared { get; } = new NullDisposable();

                public void Dispose() { }
            }
        }
    }

    internal static class SocketExtensions
    {
        public unsafe static void SetReusePort(this Socket socket)
        {
            socket.SetRawSocketOption(SOL_SOCKET, SO_REUSEPORT, 1);
            var optval = socket.GetRawSocketOption(SOL_SOCKET, SO_REUSEPORT);

            Console.WriteLine($"SO_REUSEPORT: {optval}");
        }

        public unsafe static void SetReusePortCbpf(this Socket socket)
        {
            var code = new SocketFilter[] {
                new (BpfFilter.BPF_LD | BpfFilter.BPF_W | BpfFilter.BPF_ABS, 0, 0, (uint)SkfFilter.SKF_AD_OFF + (uint)SkfFilter.SKF_AD_CPU),
                new (BpfFilter.BPF_RET | BpfFilter.BPF_A, 0, 0, 0) };

            fixed (SocketFilter* filter = &code[0])
            {
                SocketFilterProgram prog = new((ushort)code.Length, filter);
                socket.SetRawSocketOption(SOL_SOCKET, SO_ATTACH_REUSEPORT_CBPF, &prog, sizeof(SocketFilterProgram));
            }
        }

        public static unsafe void SetRawSocketOption(this Socket socket, int level, int optname, int optval)
        {
            SafeHandle handle = socket.SafeHandle;
            bool refAdded = false;
            try
            {
                handle.DangerousAddRef(ref refAdded);
                int rv = setsockopt(handle.DangerousGetHandle().ToInt32(), level, optname, &optval, sizeof(int));
                if (rv != 0)
                {
                    throw new InvalidOperationException();
                }
            }
            finally
            {
                if (refAdded)
                    handle.DangerousRelease();
            }
        }

        public static unsafe int GetRawSocketOption(this Socket socket, int level, int optname)
        {
            int optval;
            socklen_t optsize = sizeof(int);

            SafeHandle handle = socket.SafeHandle;
            bool refAdded = false;
            try
            {
                handle.DangerousAddRef(ref refAdded);
                int rv = getsockopt(handle.DangerousGetHandle().ToInt32(), level, optname, &optval, &optsize);
                if (rv != 0)
                {
                    throw new InvalidOperationException();
                }
                if (optsize < sizeof(int))
                {
                    Console.WriteLine($"Option size {optsize}");
                }
                if (optsize > sizeof(int))
                {
                    throw new InvalidOperationException("Option size to large for int");
                }

                return optval;
            }
            finally
            {
                if (refAdded)
                    handle.DangerousRelease();
            }
        }

        public static unsafe void SetRawSocketOption(this Socket socket, int level, int optname, void* optval, int size)
        {
            SafeHandle handle = socket.SafeHandle;
            bool refAdded = false;
            try
            {
                handle.DangerousAddRef(ref refAdded);
                int rv = setsockopt(handle.DangerousGetHandle().ToInt32(), level, optname, optval, size);
                if (rv != 0)
                {
                    throw new InvalidOperationException();
                }
            }
            finally
            {
                if (refAdded)
                    handle.DangerousRelease();
            }
        }
    }
}
