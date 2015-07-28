using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Inceptum.Rest
{
    public class RestResponse<TResponse>
    {
        public HttpContentHeaders Headers { get; set; }
        public TResponse Response { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public HttpResponseMessage RawResponse { get; set; }
    }
}