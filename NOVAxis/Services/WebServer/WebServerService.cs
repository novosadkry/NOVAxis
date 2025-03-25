using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

using NOVAxis.Core;

namespace NOVAxis.Services.WebServer
{
    public class WebServerService
    {
        private IOptions<WebServerOptions> Options { get; }

        public WebServerService(IOptions<WebServerOptions> options)
        {
            Options = options;
        }

        public ValueTask<string> ServeVideoDownload(Guid uuid)
        {
            var route = WebRoutes.Api.YoutubeDl.GetDownload.Replace("{uuid}", uuid.ToString());
            var uri = new Uri(new Uri(Options.Value.Endpoint), WebRoutes.Api.Base + route);

            return ValueTask.FromResult(uri.ToString());
        }
    }
}
