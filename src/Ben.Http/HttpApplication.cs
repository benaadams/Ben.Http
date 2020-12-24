using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public abstract class HttpApplication : IHttpApplication<HttpContext>
    {
        public abstract Task ProcessRequestAsync(HttpContext context);

        HttpContext IHttpApplication<HttpContext>.CreateContext(IFeatureCollection features)
        {
            HttpContext hostContext;
            if (features is IHostContextContainer<HttpContext> container)
            {
                hostContext = container.HostContext;
                if (hostContext is null)
                {
                    hostContext = new HttpContext();
                    container.HostContext = hostContext;
                }
            }
            else
            {
                // Server doesn't support pooling, so create a new Context
                hostContext = new HttpContext();
            }

            hostContext.Request = features.Get<IHttpRequestFeature>();
            hostContext.Response = features.Get<IHttpResponseFeature>();
            hostContext.ResponseBody = features.Get<IHttpResponseBodyFeature>();

            return hostContext;
        }

        void IHttpApplication<HttpContext>.DisposeContext(HttpContext context, Exception exception)
        {
            context.Reset();
        }
    }
}
