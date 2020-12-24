using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Net.Http.Headers;

using Ben.Http;

public class Application : HttpApplication
{
    public async static Task Main(string[] args)
    {
        var port = 8080;
        if (args.Length > 0)
        {
            // Set the port if specified in args
            port = int.Parse(args[0]);
        }

        using (var server = new HttpServer($"http://+:{port}"))
        {
            await server.StartAsync(new Application(), cancellationToken: default);

            // Output some verbage
            Console.WriteLine("Ben.Http standalone test application");
            Console.WriteLine();
            Console.WriteLine($"Paths /plaintext and /json; listening on port {port}");
            Console.WriteLine();
            Console.WriteLine("Press enter to exit the application");

            // Wait for keypress to exit
            Console.ReadLine();

            await server.StopAsync(cancellationToken: default);
        }
    }

    public override Task ProcessRequestAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path == "/plaintext")
        {
            return Plaintext(context);
        }
        else if (path == "/json")
        {
            return Json(context);
        }

        return NotFound(context);
    }

    public Task NotFound(HttpContext context)
    {
        // Path didn't match anything
        context.Response.StatusCode = 404;

        return Task.CompletedTask;
    }

    private static readonly byte[] _helloWorldBytes = Encoding.UTF8.GetBytes("Hello, World!");
    public async Task Plaintext(HttpContext context)
    {
        var payload = _helloWorldBytes;

        var headers = context.Response.Headers;

        headers.ContentLength = payload.Length;
        headers[HeaderNames.ContentType] = "text/plain";

        await context.ResponseBody.Writer.WriteAsync(payload);
    }

    public Task Json(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers.ContentLength = _jsonPayloadSize;
        headers[HeaderNames.ContentType] = "application/json";

        JsonSerializer.Serialize(
            GetJsonWriter(context), 
            new JsonMessage { message = "Hello, World!" }, 
            SerializerOptions);

        return Task.CompletedTask;
    }

    public struct JsonMessage
    {
        public string message { get; set; }
    }

    [ThreadStatic]
    private static Utf8JsonWriter t_writer;
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(new JsonSerializerOptions { });
    private readonly static uint _jsonPayloadSize = (uint)JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { message = "Hello, World!" }, SerializerOptions).Length;

    private Utf8JsonWriter GetJsonWriter(HttpContext context)
    {
        Utf8JsonWriter utf8JsonWriter = t_writer ??= new Utf8JsonWriter(context.ResponseBody.Writer, new JsonWriterOptions { SkipValidation = true });
        utf8JsonWriter.Reset(context.ResponseBody.Writer);
        return utf8JsonWriter;
    }
}
