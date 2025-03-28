using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Microsoft.Extensions.Options;
using Discord;

using NOVAxis.Core;
using NOVAxis.Database;
using NOVAxis.Database.Entities;
using NOVAxis.Extensions;

using YoutubeDLSharp;
using YoutubeDLSharp.Options;
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
        private readonly IOptions<DownloadOptions> _options;
        private readonly ProgramDbContext _dbContext;
        private readonly YoutubeDL _youtubeDl;

        private readonly IDictionary<IUser, byte> _pendingDownloads;

        public DownloadService(IOptions<DownloadOptions> options, ProgramDbContext dbContext)
        {
            _options = options;
            _dbContext = dbContext;
            _youtubeDl = new YoutubeDL
            {
                RestrictFilenames = true,
                OutputFolder = options.Value.OutputFolder,
                OutputFileTemplate = "%(title)s-%(epoch)s.%(ext)s"
            };
            _pendingDownloads = new ConcurrentDictionary<IUser, byte>();
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

        public async Task<Guid> DownloadVideo(IUser user, string url, string format)
        {
            await ValidateDownloadRequest(user);

            _pendingDownloads.Add(user, 1);

            try
            {
                var result = await _youtubeDl.RunVideoDownload(url, format);
                if (!result.Success) throw new DownloadException(result.ErrorOutput);

                var downloadInfo = new DownloadInfo
                {
                    SourceUrl = url,
                    UserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    Path = Path.Combine(_options.Value.OutputFolder, result.Data)
                };

                _dbContext.Downloads.Add(downloadInfo);
                await _dbContext.SaveChangesAsync();

                return downloadInfo.Uuid;
            }
            finally
            {
                _pendingDownloads.Remove(user);
            }
        }

        public async Task<Guid> DownloadAudio(IUser user, string url, AudioConversionFormat format)
        {
            await ValidateDownloadRequest(user);

            _pendingDownloads.Add(user, 1);

            try
            {
                var result = await _youtubeDl.RunAudioDownload(url, format);
                if (!result.Success) throw new DownloadException(result.ErrorOutput);

                var downloadInfo = new DownloadInfo
                {
                    SourceUrl = url,
                    UserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    Path = Path.Combine(_options.Value.OutputFolder, result.Data)
                };

                _dbContext.Downloads.Add(downloadInfo);
                await _dbContext.SaveChangesAsync();

                return downloadInfo.Uuid;
            }
            finally
            {
                _pendingDownloads.Remove(user);
            }
        }

        private async Task ValidateDownloadRequest(IUser user)
        {
            if (!await CheckAvailableSpace())
                throw new DownloadException(["Maximum output folder size reached"]);

            if (_pendingDownloads.ContainsKey(user))
                throw new DownloadException(["You already have a pending download"]);

            if (_pendingDownloads.Count > _options.Value.MaxPendingDownloads)
                throw new DownloadException(["Maximum pending downloads reached"]);
        }

        private async Task<bool> CheckAvailableSpace()
        {
            var folder = new DirectoryInfo(_options.Value.OutputFolder);
            if (!folder.Exists) folder.Create();

            return await folder.GetDirectorySizeAsync() < _options.Value.OutputFolderLimit;
        }
    }
}
