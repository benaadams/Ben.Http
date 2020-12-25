using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Ben.Http
{
    public class HttpServer : IServer
    {
        private Task? _startTask;
        private TaskCompletionSource _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private ILoggerFactory _loggerFactory;
        private IServer _server;

        public IFeatureCollection Features => _server.Features;

        public HttpServer(string listenAddress) : this(DefaultLoggerFactories.Empty)
        {
            var addresses = Features.Get<IServerAddressesFeature>();
            addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(string listenAddress, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            var addresses = Features.Get<IServerAddressesFeature>();
            addresses.Addresses.Add(listenAddress);
        }

        public HttpServer(IEnumerable<string> listenAddresses, ILoggerFactory loggerFactory) : this(loggerFactory)
        {
            var addresses = Features.Get<IServerAddressesFeature>();
            foreach (var uri in listenAddresses)
            {
                addresses.Addresses.Add(uri);
            };
        }

        private HttpServer(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _server = new KestrelServer(
                KestrelOptions.Defaults,
                new SocketTransportFactory(SocketOptions.Defaults, _loggerFactory),
                _loggerFactory);
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken = default) where TContext : notnull
            => _startTask ??= _server.StartAsync(application, cancellationToken);

        public async Task RunAsync(HttpApplication application, CancellationToken cancellationToken = default)
        {
            await StartAsync(application, cancellationToken);

            cancellationToken.UnsafeRegister(static (o) => ((HttpServer)o!)._completion.TrySetResult(), this);
            
            await _completion.Task;

            await _server.StopAsync(default);
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => _server.StopAsync(cancellationToken);

        public void Dispose() => _server.Dispose();

        private class DefaultLoggerFactories
        {
            public static ILoggerFactory Empty => new LoggerFactory();
        }

        private class KestrelOptions : IOptions<KestrelServerOptions>
        {
            public static KestrelOptions Defaults { get; } = new KestrelOptions { Value = new KestrelServerOptions() };

            public KestrelServerOptions Value { get; init; }
        }

        private class SocketOptions : IOptions<SocketTransportOptions>
        {
            public static SocketOptions Defaults { get; } = new SocketOptions { Value = new SocketTransportOptions() };

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
}
