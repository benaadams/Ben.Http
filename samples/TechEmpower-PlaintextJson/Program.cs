using System;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Net.Http.Headers;

using Ben.Http;

public class Application : HttpApplication
{
    public async static Task Main()
    {
        using (var server = new HttpServer("http://+:8080"))
        {
            await server.StartAsync(new Application(), cancellationToken: default);

            Console.WriteLine("Ben.Http Stand alone test application.");
            Console.WriteLine("Press enter to exit the application");

            Console.ReadLine();

            await server.StopAsync(cancellationToken: default);
        } 
    }

    private static readonly byte[] _helloWorldBytes = Encoding.UTF8.GetBytes("Hello, World!");

    public override async Task ProcessRequestAsync(HttpContext context)
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
}
