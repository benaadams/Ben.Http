using System;

using Ben.Http;
using SqlConnection = Npgsql.NpgsqlConnection;

var connection = Environment.GetEnvironmentVariable("connection");
var (server, app) = (new HttpServer("http://+:8080"), new HttpApp());

app.Get("/plaintext", () => "Hello, World!");

app.Get("/json", (req, res) => {
    res.Headers.ContentLength = 27;
    return res.Json(new Note { message = "Hello, World!" });
});

app.Get("/fortunes", async (req, res) => {
    using SqlConnection conn = new (connection);
    var model = await conn.QueryAsync<(int id, string message)>("SELECT id, message FROM fortune");
    model.Add((0, "Additional fortune added at request time."));
    model.Sort((x, y) => string.CompareOrdinal(x.message, y.message));
    MustacheTemplates.RenderFortunes(model, res.Writer);
});

await server.RunAsync(app);

struct Note { public string message { get; set; } }