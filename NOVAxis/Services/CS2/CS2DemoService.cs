using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NOVAxis.Database;
using NOVAxis.Database.Entities;
using NOVAxis.Extensions;

namespace NOVAxis.Services.CS2
{
    public class CS2DemoService
    {
        private readonly ProgramDbContext _dbContext;
        private readonly ILogger<CS2HostedService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CS2DemoProcessorService _demoProcessorService;

        public CS2DemoService(
            ProgramDbContext dbContext,
            ILogger<CS2HostedService> logger,
            IHttpClientFactory httpClientFactory,
            CS2DemoProcessorService demoProcessorService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _demoProcessorService = demoProcessorService;
        }

        public async Task<bool> AlreadyProcessed(string demoUrl)
        {
            return await _dbContext.CS2Matches
                .AnyAsync(s => s.DemoUrl == demoUrl);
        }

        public async Task ProcessDemoAsync(CS2DemoQueue message)
        {
            _logger.Info($"Downloading demo: {message.DemoUrl}");

            // Download the demo file
            var demoPath = await DownloadDemoAsync(message.DemoUrl);

            try
            {
                // Process the downloaded demo
                _logger.Info($"Processing demo file: {message.DemoUrl}");
                await _demoProcessorService.ProcessDemoAsync(message.DemoUrl, demoPath);
            }
            finally
            {
                // Clean up the downloaded file
                File.Delete(demoPath);
            }

            _logger.Info($"Successfully processed demo: {message.DemoUrl}");
        }

        private async Task<string> DownloadDemoAsync(string url)
        {
            try
            {
                var tempPath = Path.GetTempFileName();
                var httpClient = _httpClientFactory.CreateClient();

                _logger.Info($"Downloading demo from: {url}");

                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await using var bzip2 = new BZip2InputStream(responseStream);

                await using var fileStream = File.Create(tempPath);
                await bzip2.CopyToAsync(fileStream);

                _logger.Info($"Downloaded demo to: {tempPath}");

                return tempPath;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error downloading demo from {url}", ex);
                throw;
            }
        }
    }
}
