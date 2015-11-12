using System;
using System.Net.Http;

namespace Inceptum.Rest
{
    public class NodeRequestResult
    {
        public Uri Uri
        {
            get { return Request == null ? null : Request.RequestUri; }
        }
        public HttpRequestMessage Request { get; set; }
        public Exception Exception { get; set; }
        public HttpResponseMessage Response { get; set; }

        public NodeRequestResult(HttpRequestMessage request)
        {
            Request = request;
        }
    }
}