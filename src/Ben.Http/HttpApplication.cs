using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public abstract class HttpApplication : IHttpApplication<Context>
    {
        public abstract Task ProcessRequestAsync(Context context);

        Context IHttpApplication<Context>.CreateContext(IFeatureCollection features)
        {
            Context context;
            if (features is IHostContextContainer<Context> container)
            {
                // The server allows us to store the HttpContext on the connection
                // between requests so we don't have to reallocate it each time.
                context = container.HostContext;
                if (context is null)
                {
                    context = new Context();
                    container.HostContext = context;
                }
            }
            else
            {
                // Server doesn't support pooling, so create a new Context
                context = new Context();
            }

            context.Initialize(features);

            return context;
        }

        void IHttpApplication<Context>.DisposeContext(Context context, Exception exception)
        {
            // As we may be pooling the HttpContext above; Reset its settings.
            context.Reset();
        }
    }
}
