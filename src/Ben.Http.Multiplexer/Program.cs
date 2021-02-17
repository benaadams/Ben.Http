using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

using Tmds.Linux;

using static Tmds.Linux.LibC;

namespace Ben.Http.Multiplexer
{
    class Program
    {
        static void Main(string[] args)
        {
            string listenAddress = args[1];
            string path = args[0];

            var parsedAddress = BindingAddress.Parse(listenAddress);
            var https = false;

            if (parsedAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                https = true;
            }
            else if (!parsedAddress.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"UnsupportedAddressScheme {listenAddress}");
            }

            if (!IPAddress.TryParse(parsedAddress.Host, out var ip))
            {
                ip = IPAddress.Any;
            }

        }

        private unsafe static void CreateWorkers(string path, IPAddress ip, int port)
        {
            cpu_set_t online_cpus, cpu;
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

            // fork a new child/worker process for each available cpu
            for (int i = 0; i < rel_to_abs_cpu.Length; i++)
            {
                var fd = CreateSocket(ip, (ushort)port);
                var startInfo = new ProcessStartInfo();
                startInfo.EnvironmentVariables["LISTEN_FD"] = fd.ToString(CultureInfo.InvariantCulture);
                startInfo.UseShellExecute = false;
                startInfo.FileName = path;
                Process.Start(startInfo);
            }

        }

        private unsafe static int CreateSocket(IPAddress ip, ushort port)
        {
            var sk = socket(AF_INET, SOCK_STREAM, 0);
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

            sockaddr_in addr = default;
            addr.sin_family = AF_INET;
            if (ip != IPAddress.Any)
            {
                if (!ip.TryWriteBytes(new Span<byte>(addr.sin_addr.s_addr, 4), out int bytesWritten) || bytesWritten != 4)
                {
                    throw new ArgumentOutOfRangeException($"Invalid IPv4 address {ip}");
                }
            }

            addr.sin_port = htons(port);

            rv = bind(sk, (sockaddr*)&addr, sizeof(sockaddr));
            if (rv < 0)
            {
                throw new SocketException(rv);
            }

            return sk;
        }

        private static unsafe int ForkWorkers()
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
    }
}
