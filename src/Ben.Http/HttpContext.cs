using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http.Features;

namespace Ben.Http
{
    public class HttpContext
    {
        public IHttpRequestFeature Request { get; internal set; }
        public IHttpResponseFeature Response { get; internal set; }
        public IHttpResponseBodyFeature ResponseBody { get; internal set; }

        public void Reset()
        {
            Request = null;
            Response = null;
            ResponseBody = null;
        }
    }
}
