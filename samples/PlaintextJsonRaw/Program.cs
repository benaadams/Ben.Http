using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using static System.Console;

using Ben.Http;

var port = 8080;

var server = new HttpServer($"http://+:{port}");
var app = new HttpApp();

// Assign routes
app.Get("/plaintext", Plaintext);
app.Get("/json", Json);

Write($"{server} {app}"); // Display listening info

await server.RunAsync(app);

// Route methods
async Task Plaintext(Request request, Response response)
{
    var payload = Settings.HelloWorld;

    var headers = response.Headers;

    headers.ContentLength = payload.Length;
    headers[HeaderNames.ContentType] = "text/plain";

    await response.Writer.WriteAsync(payload);
}

static Task Json(Request request, Response response)
{
    var headers = response.Headers;

    headers.ContentLength = 27;
    headers[HeaderNames.ContentType] = "application/json";

    return JsonSerializer.SerializeAsync(
        response.Stream, 
        new JsonMessage { message = "Hello, World!" }, 
        Settings.SerializerOptions);
}

// Settings and datastructures
struct JsonMessage { public string message { get; set; } }

static class Settings
{
    public static readonly byte[] HelloWorld = Encoding.UTF8.GetBytes("Hello, World!");
    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(new JsonSerializerOptions { });
}