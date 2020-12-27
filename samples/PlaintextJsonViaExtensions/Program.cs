using System;
using Ben.Http;

var (server, app) = (new HttpServer($"http://+:8080"), new HttpApp());

// Assign routes
app.Get("/plaintext", () => "Hello, World!");

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.Json(new Note { message = "Hello, World!" });
});

Console.Write($"{server} {app}"); // Display listening info

// Start the server
await server.RunAsync(app);

// Datastructures
struct Note { public string message { get; set; } }