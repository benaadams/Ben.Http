using System.IO;
using System.IO.Pipelines;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public class Context
    {
        public Request Request { get; } = new Request();
        public Response Response { get; } = new Response();

        internal void Initialize(IFeatureCollection features)
        {
            Request.Initialize(features);
            Response.Initialize(features);
        }

        public void Reset()
        {
            Request.Reset();
            Response.Reset();
        }
    }

    public class Request
    {
        private IFeatureCollection _features = null!;
        private IHttpRequestFeature? _request;
        private IHttpRequestFeature RequestFeature => _request ??= _features.Get<IHttpRequestFeature>();

        internal void Initialize(IFeatureCollection features) => _features = features;

        /// <summary>
        /// The HTTP-version as defined in RFC 7230. E.g. "HTTP/1.1"
        /// </summary>
        public string Protocol => RequestFeature.Protocol;

        /// <summary>
        /// The request uri scheme. E.g. "http" or "https". Note this value is not included
        /// in the original request, it is inferred by checking if the transport used a TLS
        /// connection or not.
        /// </summary>
        public string Scheme => RequestFeature.Scheme;

        /// <summary>
        /// The request method as defined in RFC 7230. E.g. "GET", "HEAD", "POST", etc..
        /// </summary>
        public string Method => RequestFeature.Method;

        /// <summary>
        /// The first portion of the request path associated with application root. The value
        /// is un-escaped. The value may be string.Empty.
        /// </summary>
        public string PathBase => RequestFeature.PathBase;

        /// <summary>
        /// The portion of the request path that identifies the requested resource. The value
        /// is un-escaped. The value may be string.Empty if PathBase
        /// contains the full path.
        /// </summary>
        public string Path => RequestFeature.Path;

        /// <summary>
        /// The query portion of the request-target as defined in RFC 7230. The value may
        /// be string.Empty. If not empty then the leading '?' will be included. The value
        /// is in its original form, without un-escaping.
        /// </summary>
        public string QueryString => RequestFeature.QueryString;

        /// <summary>
        /// Headers included in the request, aggregated by header name. The values are not
        /// split or merged across header lines. E.g. The following headers: HeaderA: value1,
        /// value2 HeaderA: value3 Result in Headers["HeaderA"] = { "value1, value2", "value3" }
        /// </summary>
        public IHeaderDictionary Headers => RequestFeature.Headers;
        /// <summary>
        /// A System.IO.Stream representing the request body, if any. Stream.Null may be
        /// used to represent an empty request body.
        /// </summary>
        public Stream Body => RequestFeature.Body;

        internal void Reset() => _request = null;
    }

    public class Response
    {
        private IFeatureCollection _features = null!;
        private IHttpResponseFeature? _response;
        private IHttpResponseBodyFeature? _responseBody;

        private IHttpResponseFeature ResponseFeature => _response ??= _features.Get<IHttpResponseFeature>();
        private IHttpResponseBodyFeature ResponseBody => _responseBody ??= _features.Get<IHttpResponseBodyFeature>();

        internal void Initialize(IFeatureCollection features) => _features = features;

        /// <summary>
        ///  The status-code as defined in RFC 7230. The default value is 200.
        /// </summary>
        public int StatusCode 
        { 
            get => ResponseFeature.StatusCode; 
            set => ResponseFeature.StatusCode = value; 
        }

        /// <summary>
        /// The response headers to send. Headers with multiple values will be emitted as multiple headers.
        /// </summary>
        public IHeaderDictionary Headers => ResponseFeature.Headers;

        /// <summary>
        /// The System.IO.Stream for writing the response body.
        /// </summary>
        public Stream Stream => ResponseBody.Stream;

        /// <summary>
        /// A System.IO.Pipelines.PipeWriter representing the response body, if any.
        /// </summary>
        public PipeWriter Writer => ResponseBody.Writer;

        internal void Reset()
        {
            _response = null;
            _responseBody = null;
        }
    }
}
