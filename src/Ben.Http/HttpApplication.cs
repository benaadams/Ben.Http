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
            HttpContext context;
            if (features is IHostContextContainer<HttpContext> container)
            {
                // The server allows us to store the HttpContext on the connection
                // between requests so we don't have to reallocate it each time.
                context = container.HostContext;
                if (context is null)
                {
                    context = new HttpContext();
                    container.HostContext = context;
                }
            }
            else
            {
                // Server doesn't support pooling, so create a new Context
                context = new HttpContext();
            }

            context.Initialize(features);

            return context;
        }

        void IHttpApplication<HttpContext>.DisposeContext(HttpContext context, Exception exception)
        {
            // As we may be pooling the HttpContext above; Reset its settings.
            context.Reset();
        }
    }
}
