using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

using Ben.Http;

public class Program
{
    private static SemaphoreSlim _semaphore = new SemaphoreSlim(0);

    public async static Task Main()
    {
        var server = new HttpServer();
        await server.StartAsync(new Application(), default);
        await _semaphore.WaitAsync();
    }
}

public class Application : IHttpApplication<Context>
{
    private static readonly byte[] _helloWorldBytes = Encoding.UTF8.GetBytes("Hello, World!");

    public async Task ProcessRequestAsync(Context context)
    {
        // Should do some routing here...

        var response = context.Response;

        response.StatusCode = 200;

        var headers = response.Headers;

        headers[HeaderNames.ContentType] = "text/plain";

        var payload = _helloWorldBytes;
        headers.ContentLength = payload.Length;

        await context.ResponseBody.Writer.WriteAsync(payload);
    }

    public Context CreateContext(IFeatureCollection features)
    {
        Context hostContext;
        if (features is IHostContextContainer<Context> container)
        {
            hostContext = container.HostContext;
            if (hostContext is null)
            {
                hostContext = new Context();
                container.HostContext = hostContext;
            }
        }
        else
        {
            // Server doesn't support pooling, so create a new Context
            hostContext = new Context();
        }

        hostContext.Request = features.Get<IHttpRequestFeature>();
        hostContext.Response = features.Get<IHttpResponseFeature>();
        hostContext.ResponseBody = features.Get<IHttpResponseBodyFeature>();

        return hostContext;
    }

    public void DisposeContext(Context context, Exception exception)
    {
        context.Reset();
    }
}

public class Context
{
    public IHttpRequestFeature Request { get; internal set; }
    public IHttpResponseFeature Response { get; internal set; }
    public IHttpResponseBodyFeature ResponseBody { get; internal set; }

    public void Reset()
    {
        Request = null;
        Response = null;
        ResponseBody = null;
    }
}
