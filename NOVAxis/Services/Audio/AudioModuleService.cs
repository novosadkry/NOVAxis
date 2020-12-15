﻿using System;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Modules.Audio;

using Discord;
using Discord.WebSocket;

using Victoria;
using Victoria.Enums;
using Victoria.EventArgs;

namespace NOVAxis.Services.Audio
{
    public class AudioModuleService
    {
        public long AudioTimeout { get; }

        private readonly LavaNode _lavaNodeInstance;
        private readonly Cache<ulong, Lazy<AudioContext>> _guilds;

        public AudioModuleService(LavaNode lavaNodeInstance)
        {
            AudioTimeout = Program.Config.AudioTimeout;
            _guilds = new Cache<ulong, Lazy<AudioContext>>();

            _lavaNodeInstance = lavaNodeInstance;
            _lavaNodeInstance.OnTrackEnded += AudioModuleService_TrackEnd;

            Program.Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;
        }

        ~AudioModuleService()
        {
            _lavaNodeInstance.OnTrackEnded -= AudioModuleService_TrackEnd;
            Program.Client.UserVoiceStateUpdated -= AudioModuleService_UserVoiceStateUpdated;
        }

        public AudioContext this[ulong id]
        {
            get => _guilds.GetOrAdd(id, new Lazy<AudioContext>(() => new AudioContext(id))).Value;
            set => _guilds[id] = new Lazy<AudioContext>(value);
        }

        public void Remove(ulong id)
        {
            _guilds.Remove(id);
        }

        private async Task AudioModuleService_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user != Program.Client.CurrentUser || before.VoiceChannel == null)
                return;

            if (_lavaNodeInstance.TryGetPlayer(before.VoiceChannel.Guild, out LavaPlayer player))
            {
                if (after.VoiceChannel == null && player.PlayerState == PlayerState.Playing)
                {
                    AudioContext context = this[before.VoiceChannel.Guild.Id];

                    Remove(context.GuildId);
                    context.Dispose();

                    await _lavaNodeInstance.LeaveAsync(before.VoiceChannel);
                }
            }
        }

        private async Task AudioModuleService_TrackEnd(TrackEndedEventArgs args)
        {
            if (args.Player.VoiceChannel == null)
                return;

            var audioContext = this[args.Player.VoiceChannel.GuildId];

            if (audioContext.Queue.Count == 0)
                return;

            var prevTrack = audioContext.Queue.Dequeue();

            switch (audioContext.Repeat)
            {
                case RepeatMode.Once:
                    audioContext.Queue.AddFirst(prevTrack);
                    audioContext.Repeat = RepeatMode.None;
                    break;

                case RepeatMode.First:
                    audioContext.Queue.AddFirst(prevTrack);
                    break;

                case RepeatMode.Queue:
                    audioContext.Queue.Enqueue(prevTrack);
                    break;
            }

            if (audioContext.Queue.Count > 0)
            {
                AudioTrack nextTrack = audioContext.Queue.Peek();
                await args.Player.PlayAsync(nextTrack);

                var statusEmoji = AudioModule.GetStatusEmoji(audioContext, args.Player);

                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{nextTrack.Title}")
                    .WithUrl(nextTrack.Url)
                    .WithThumbnailUrl(nextTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = nextTrack.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{nextTrack.Duration}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = nextTrack.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost:",
                            Value = $"{args.Player.Volume}%",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Stav:",
                            Value = $"{string.Join(' ', statusEmoji)}",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně dokončen").Build());
            }

            audioContext.Timer.Reset();
        }
    }
}
