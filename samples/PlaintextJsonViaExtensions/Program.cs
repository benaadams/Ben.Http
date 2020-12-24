using System;
using System.Threading;
using System.Threading.Tasks;

using StringLiteral;

using Ben.Http;

public partial class Application : HttpApplication
{
    [Utf8("Hello, World!")]
    public static partial ReadOnlySpan<byte> Utf8HelloWorld();

    public Task Plaintext(Request request, Response response)
        => response.TextResult(Utf8HelloWorld());

    public Task Json(Request request, Response response)
        => response.JsonResult(new JsonMessage { message = "Hello, World!" });

    public Task NotFound(Request request, Response response)
    {
        // Path didn't match anything
        response.StatusCode = 404;

        return Task.CompletedTask;
    }

    private readonly static SemaphoreSlim _cancel = new(initialCount: 0);

    public async static Task Main(string[] args)
    {
        var port = 8080;
        if (args.Length > 0)
        {
            // Set the port if specified in args
            port = int.Parse(args[0]);
        }

        // Set Ctrl-C to start the shutdown
        Console.CancelKeyPress += (_, _) => _cancel.Release();

        using (var server = new HttpServer($"http://+:{port}"))
        {
            await server.StartAsync(new Application(), cancellationToken: default);

            // Output some verbage
            Console.WriteLine($"Paths /plaintext and /json; listening on port {port}");

            await _cancel.WaitAsync();

            await server.StopAsync(cancellationToken: default);
        }
    }

    // Request loop; note this is called in parallel so should be stateless.
    // State holding for the request should be setup in the HttpContext
    public override Task ProcessRequestAsync(Context context)
    {
        // Do routing in this override method
        var path = context.Request.Path;
        if (path == "/plaintext")
        {
            return Plaintext(context.Request, context.Response);
        }
        else if (path == "/json")
        {
            return Json(context.Request, context.Response);
        }

        return NotFound(context.Request, context.Response);
    }

    public struct JsonMessage
    {
        public string message { get; set; }
    }
}
