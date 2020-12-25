using System;
using StringLiteral;
using static System.Console;

using Ben.Http;

var port = 8080;

var (server, app) = (new HttpServer($"http://+:{port}"), new HttpApplication());

// Assign routes
app.Get("/plaintext", (req, res)
    => res.TextResult(Settings.HelloWorld()));

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.JsonResult(new JsonMessage { message = "Hello, World!" });
});

// Output info
Write($"{server} {app}");

// Start the server
await server.RunAsync(app);

// Settings and datastructures
static partial class Settings
{
    [Utf8("Hello, World!")]
    public static partial ReadOnlySpan<byte> HelloWorld();
}

struct JsonMessage { public string message { get; set; } }