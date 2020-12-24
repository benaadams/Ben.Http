using System;
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
        private ILoggerFactory _loggerFactory;
        private IServer _server;

        public IFeatureCollection Features => _server.Features;

        public HttpServer()
        {
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(new ConsoleLoggerProvider(LoggerOptions.Default));
            _server = new KestrelServer(
                KestrelOptions.Defaults, 
                new SocketTransportFactory(SocketOptions.Defaults, _loggerFactory), 
                _loggerFactory);


            var addresses = Features.Get<IServerAddressesFeature>();
            addresses.Addresses.Add("http://+");
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
            => _server.StartAsync(application, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => _server.StopAsync(cancellationToken);

        public void Dispose() => _server.Dispose();

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
