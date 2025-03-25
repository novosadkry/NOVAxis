using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Services.Download;

using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

namespace NOVAxis.Services.WebServer
{
    public class WebDownloadController : WebApiController
    {
        private DownloadService _downloadService;

        protected override void OnBeforeHandler()
        {
            base.OnBeforeHandler();

            _downloadService = HttpContext.GetIoCScope().ServiceProvider
                .GetRequiredService<DownloadService>();
        }

        [Route(HttpVerbs.Get, WebRoutes.Api.YoutubeDl.GetDownload)]
        public async Task GetDownload(Guid uuid)
        {
            var downloadInfo = await _downloadService.GetDownloadInfo(uuid)
                ?? throw HttpException.NotFound("Download not found or expired");;

            if (!File.Exists(downloadInfo.Path))
                throw HttpException.NotFound("File not found");

            var filename = Path.GetFileName(downloadInfo.Path);

            HttpContext.Response.ContentType = "application/octet-stream";
            HttpContext.Response.Headers.Add("Content-Disposition", $"attachment; filename={filename}");

            await using var fileStream = File.OpenRead(downloadInfo.Path);
            await using var stream = HttpContext.OpenResponseStream();

            await fileStream.CopyToAsync(stream);
        }
    }
}
