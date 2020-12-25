using System;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Net.Http.Headers;

namespace Ben.Http
{
    public static class ResponseExtensions
    {
        public static Task Text(this Response response, ReadOnlySpan<byte> utf8String)
        {
            var headers = response.Headers;

            headers.ContentLength = utf8String.Length;
            headers[HeaderNames.ContentType] = "text/plain";

            var writer = response.Writer;
            var output = writer.GetSpan(utf8String.Length);

            utf8String.CopyTo(output);
            writer.Advance(utf8String.Length);

            return Task.CompletedTask;
        }

        public static Task Json<TValue>(this Response response, TValue value)
        {
            response.Headers[HeaderNames.ContentType] = "application/json";

            JsonSerializer.Serialize(GetJsonWriter(response), value, SerializerOptions);

            return Task.CompletedTask;
        }

        public static Task NotFound(this Response response)
        {
            response.StatusCode = 404;

            return Task.CompletedTask;
        }

        [ThreadStatic]
        private static Utf8JsonWriter t_writer = null!;
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(new JsonSerializerOptions { });

        private static Utf8JsonWriter GetJsonWriter(Response response)
        {
            Utf8JsonWriter utf8JsonWriter = t_writer ??= new Utf8JsonWriter(response.Writer, new JsonWriterOptions { SkipValidation = true });
            utf8JsonWriter.Reset(response.Writer);
            return utf8JsonWriter;
        }
    }
}
