using System;
using StringLiteral;
using static System.Console;

using Ben.Http;

var port = 8080;

var server = new HttpServer($"http://+:{port}");
var app = new HttpApplication();

app.Get("/plaintext", (req, res)
    => res.TextResult(Settings.HelloWorld()));

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.JsonResult(new JsonMessage { message = "Hello, World!" });
});

WriteLine($"Listening on port {port}, paths:\n=> {string.Join("\n=> ", app.Paths)}");

await server.RunAsync(app);

static partial class Settings
{
    [Utf8("Hello, World!")]
    public static partial ReadOnlySpan<byte> HelloWorld();
}

struct JsonMessage { public string message { get; set; } }