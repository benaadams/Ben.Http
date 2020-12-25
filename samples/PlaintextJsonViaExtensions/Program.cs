using System;
using Ben.Http;

var port = 8080;

var (server, app) = (new HttpServer($"http://+:{port}"), new HttpApp());

// Assign routes
app.Get("/plaintext", () => "Hello, World!");

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.Json(new Message { message = "Hello, World!" });
});

Console.Write($"{server} {app}"); // Display listening info

// Start the server
await server.RunAsync(app);

// Datastructures
struct Message { public string message { get; set; } }