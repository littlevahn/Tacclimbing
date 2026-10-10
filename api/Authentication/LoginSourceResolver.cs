using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace Tacc.Api.Authentication;

public sealed class LoginSourceResolver(bool isAzureAppService)
{
    public string Resolve(HttpRequestData request)
    {
        // App Service's frontend overwrites CLIENT-IP. Trust it only in that hosting environment.
        // X-Forwarded-For, X-Azure-ClientIP, URL hostnames, and local caller headers are never trusted.
        if (isAzureAppService && request.Headers.TryGetValues("CLIENT-IP", out var headers))
        {
            var values = headers.ToArray();
            if (values.Length == 1 && values[0].Length <= 80)
            {
                var value = values[0].Trim();
                IPAddress? address = null;
                if (IPAddress.TryParse(value, out var ip)) address = ip;
                else if (IPEndPoint.TryParse(value, out var endpoint)) address = endpoint.Address;
                if (address is not null)
                    return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
            }
        }
        // Isolated HttpRequestData has no socket address. Unknown/local sources share a safe bucket.
        return "unknown-source";
    }
}
