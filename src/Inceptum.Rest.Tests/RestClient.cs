using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Inceptum.Rest.Tests
{
    internal class RestClient : RestClientBase
    {
        public RestClient(string[] addresses, int failTimeout = 15000, int farmRequestTimeout = 120000,
            long singleAddressTimeout = 60000, int delayTimeout = 5000, Func<HttpMessageHandler> handlerFactory = null)
            : base(addresses, failTimeout, farmRequestTimeout, singleAddressTimeout, delayTimeout, handlerFactory)
        {
        }

        public async Task<RestResponse<TResult>> SendAsync<TResult>(Func<HttpRequestMessage> requestFactory, CultureInfo cultureInfo)
        {
            return await SendAsync<TResult>(requestFactory, cultureInfo, CancellationToken.None);
        }

        public Task<RestResponse<string>> GetData(Uri relativeUri, CultureInfo cultureInfo, CancellationToken cancellationToken)
        {
            return GetData<string>(relativeUri, cultureInfo, cancellationToken);
        }

        public Task<RestResponse<T>> GetObject<T>(Uri relativeUri, CultureInfo cultureInfo, CancellationToken cancellationToken)
        {
            return base.GetData<T>(relativeUri, cultureInfo, cancellationToken);
        }
    }
}