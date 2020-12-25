using System;
using StringLiteral;
using static System.Console;

using Ben.Http;

var port = 8080;

var (server, app) = (new HttpServer($"http://+:{port}"), new HttpApp());

// Assign routes
app.Get("/plaintext", (req, res)
    => res.Text(Settings.HelloWorld()));

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.Json(new Message { message = "Hello, World!" });
});

Write($"{server} {app}"); // Display listening info

// Start the server
await server.RunAsync(app);

// Settings and datastructures
static partial class Settings
{
    [Utf8("Hello, World!")]
    public static partial ReadOnlySpan<byte> HelloWorld();
}

struct Message { public string message { get; set; } }