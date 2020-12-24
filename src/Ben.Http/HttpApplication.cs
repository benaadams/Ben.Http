using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Ben.Http
{
    public abstract class HttpApplication : IHttpApplication<HttpContext>
    {
        public abstract Task ProcessRequestAsync(HttpContext context);

        public HttpContext CreateContext(IFeatureCollection features)
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

        public void DisposeContext(HttpContext context, Exception exception)
        {
            context.Reset();
        }
    }
}
