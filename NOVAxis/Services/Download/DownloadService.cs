using System;
using System.IO;
using System.Threading.Tasks;

using Discord;

using NOVAxis.Database;
using NOVAxis.Database.Entities;

using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace NOVAxis.Services.Download
{
    public class DownloadException : Exception
    {
        public string[] ErrorOutput { get; }

        public DownloadException(string[] errorOutput)
            : base("An error occurred during download")
        {
            ErrorOutput = errorOutput;
        }
    }

    public class DownloadService
    {
        private const string OutputFolder = "downloads";
        private readonly ProgramDbContext _dbContext;
        private readonly YoutubeDL _youtubeDl;

        public DownloadService(ProgramDbContext dbContext)
        {
            _dbContext = dbContext;
            _youtubeDl = new YoutubeDL
            {
                RestrictFilenames = true,
                OutputFolder = OutputFolder,
                OutputFileTemplate = "%(title)s-%(epoch)s.%(ext)s"
            };
        }

        public async Task<DownloadInfo> GetDownloadInfo(Guid uuid)
        {
            return await _dbContext.Downloads.FindAsync(uuid);
        }

        public async Task<VideoData> DownloadVideoMetadata(string url)
        {
            var result = await _youtubeDl.RunVideoDataFetch(url);
            if (!result.Success) throw new DownloadException(result.ErrorOutput);

            return result.Data;
        }

        public async Task<Guid> DownloadVideo(IUser user, string url)
        {
            var result = await _youtubeDl.RunVideoDownload(url);
            if (!result.Success) throw new DownloadException(result.ErrorOutput);

            var downloadInfo = new DownloadInfo
            {
                SourceUrl = url,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                Path = Path.Combine(OutputFolder, result.Data)
            };

            _dbContext.Downloads.Add(downloadInfo);
            await _dbContext.SaveChangesAsync();

            return downloadInfo.Uuid;
        }
    }
}
