using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public class HttpContext
    {
        private IFeatureCollection _features = null!;

        private IHttpRequestFeature? _request;
        private IHttpResponseFeature? _response;
        private IHttpResponseBodyFeature? _responseBody;

        internal void Initialize(IFeatureCollection features)
        {
            _features = features;
        }

        public IHttpRequestFeature Request => _request ??= _features.Get<IHttpRequestFeature>();
        public IHttpResponseFeature Response => _response ??= _features.Get<IHttpResponseFeature>();
        public IHttpResponseBodyFeature ResponseBody => _responseBody ??= _features.Get<IHttpResponseBodyFeature>();

        public void Reset()
        {
            _request = null;
            _response = null;
            _responseBody = null;
        }
    }
}
