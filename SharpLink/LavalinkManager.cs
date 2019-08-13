﻿using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using SharpLink.Enums;
using SharpLink.Events;
using SharpLink.Stats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpLink
{
    public class LavalinkManager
    {
        private LavalinkWebSocket webSocket;
        private LavalinkManagerConfig config;
        private BaseSocketClient baseDiscordClient;
        private SemaphoreSlim playerLock = new SemaphoreSlim(1, 1);
        private Dictionary<ulong, LavalinkPlayer> players = new Dictionary<ulong, LavalinkPlayer>();
        private int connectionWait = 3000;
        private CancellationTokenSource lavalinkCancellation;
        private HttpClient client = new HttpClient();
        internal Logger logger;

        #region PUBLIC_EVENTS 
        public event AsyncEvent<LavalinkPlayer, LavalinkTrack, long> PlayerUpdate;
        public event AsyncEvent<LavalinkPlayer, LavalinkTrack, string> TrackEnd;
        public event AsyncEvent<LavalinkPlayer, LavalinkTrack, long> TrackStuck;
        public event AsyncEvent<LavalinkPlayer, LavalinkTrack, string> TrackException;
        public event AsyncEvent<LogMessage> Log;
        public event AsyncEvent<LavalinkStats> Stats;
        #endregion

        /// <summary>
        /// Initiates a new Lavalink node connection
        /// </summary>
        /// <param name="discordClient"></param>
        /// <param name="config"></param>
        public LavalinkManager(DiscordSocketClient discordClient, LavalinkManagerConfig config = null)
        {
            this.config = config ?? new LavalinkManagerConfig();
            baseDiscordClient = discordClient;

            SetupManager();
        }

        public LavalinkManager(DiscordShardedClient discordShardedClient, LavalinkManagerConfig config = null)
        {
            this.config = config ?? new LavalinkManagerConfig();
            baseDiscordClient = discordShardedClient;

            SetupManager();
        }

        private void SetupManager()
        {
            // Setup the logger and rest client
            logger = new Logger(this, "Lavalink");
            client.DefaultRequestHeaders.Add("Authorization", config.Authorization);

            // Setup the socket client events
            baseDiscordClient.VoiceServerUpdated += async (voiceServer) =>
            {
                logger.Log($"VOICE_SERVER_UPDATE({voiceServer.Guild.Id}, Updating Session)", LogSeverity.Debug);

                if (players.TryGetValue(voiceServer.Guild.Id, out LavalinkPlayer player))
                    await player.UpdateSessionAsync(SessionChange.Connect, voiceServer);
            };

            baseDiscordClient.UserVoiceStateUpdated += async (user, oldVoiceState, newVoiceState) =>
            {
                // We only need voice state updates for the current user
                if (user.Id == baseDiscordClient.CurrentUser.Id)
                {
                    if (oldVoiceState.VoiceChannel == null && newVoiceState.VoiceChannel != null)
                    {
                        logger.Log($"VOICE_STATE_UPDATE({newVoiceState.VoiceChannel.Guild.Id}, Connected)", LogSeverity.Debug);

                        // Connected
                        if (players.TryGetValue(newVoiceState.VoiceChannel.Guild.Id, out LavalinkPlayer player))
                            player.SetSessionId(newVoiceState.VoiceSessionId);
                    }
                    else if (oldVoiceState.VoiceChannel != null && newVoiceState.VoiceChannel == null)
                    {
                        logger.Log($"VOICE_STATE_UPDATE({oldVoiceState.VoiceChannel.Guild.Id}, Disconnected)", LogSeverity.Debug);

                        // Disconnected
                        if (players.TryGetValue(oldVoiceState.VoiceChannel.Guild.Id, out LavalinkPlayer player) && !string.IsNullOrEmpty(player.GetSessionId()))
                        {
                            player.SetSessionId("");
                            await player.UpdateSessionAsync(SessionChange.Disconnect, oldVoiceState.VoiceChannel.Guild.Id);
                            await RemovePlayerAsync(oldVoiceState.VoiceChannel.Guild.Id, false);
                        }
                    }
                    else if (oldVoiceState.VoiceChannel != null && newVoiceState.VoiceChannel != null && oldVoiceState.VoiceChannel.Id == newVoiceState.VoiceChannel.Id)
                    {
                        // Reconnected
                        if (players.TryGetValue(oldVoiceState.VoiceChannel.Guild.Id, out LavalinkPlayer player) && string.IsNullOrEmpty(player.GetSessionId()))
                        {
                            logger.Log($"VOICE_STATE_UPDATE({newVoiceState.VoiceChannel.Guild.Id}, Reconnected)", LogSeverity.Debug);
                            player.SetSessionId(newVoiceState.VoiceSessionId);
                        }
                    }
                    else if (oldVoiceState.VoiceChannel != null && newVoiceState.VoiceChannel != null && oldVoiceState.VoiceChannel.Id != newVoiceState.VoiceChannel.Id)
                    {
                        logger.Log($"VOICE_STATE_UPDATE({newVoiceState.VoiceChannel.Guild.Id}, Moved)", LogSeverity.Debug);

                        // Moved
                        if (players.TryGetValue(oldVoiceState.VoiceChannel.Guild.Id, out LavalinkPlayer player))
                            await player.UpdateSessionAsync(SessionChange.Moved, newVoiceState.VoiceChannel);
                    }
                }
            };

            if (baseDiscordClient is DiscordShardedClient)
            {
                DiscordShardedClient shardedClient = baseDiscordClient as DiscordShardedClient;

                shardedClient.ShardDisconnected += async (exception, client) =>
                {
                    await playerLock.WaitAsync();

                    // Disconnect all the players associated with this shard
                    foreach(SocketGuild guild in client.Guilds)
                    {
                        if (players.TryGetValue(guild.Id, out LavalinkPlayer player))
                        {
                            await player.DisconnectAsync(true);
                        }
                    }

                    playerLock.Release();
                };
            }
            else if (baseDiscordClient is DiscordSocketClient)
            {
                DiscordSocketClient client = baseDiscordClient as DiscordSocketClient;

                client.Disconnected += async (exception) =>
                {
                    await playerLock.WaitAsync();

                    // Since this is a single shard we'll disconnect all players
                    foreach (KeyValuePair<ulong, LavalinkPlayer> player in players.ToList())
                    {
                        await player.Value.DisconnectAsync(true);
                    }

                    playerLock.Release();
                };
            }
        }

        internal async Task PlayTrackAsync(string trackId, ulong guildId)
        {
            JObject data = new JObject();
            data.Add("op", "play");
            data.Add("guildId", guildId.ToString());
            data.Add("track", trackId);

            await webSocket.SendAsync(data.ToString());
        }

        internal BaseSocketClient GetDiscordClient()
        {
            return baseDiscordClient;
        }

        internal LavalinkWebSocket GetWebSocket()
        {
            return webSocket;
        }

        internal void InvokeLog(LogMessage message)
        {
            Log?.InvokeAsync(message);
        }

        internal LavalinkManagerConfig GetConfig()
        {
            return config;
        }

        internal async Task RemovePlayerAsync(ulong guildId, bool semLock)
        {
            // The purpose of the passed semLock boolean is to prevent a problem where the disconnect event
            // already has it locked for us to use and it's simply signalling that we don't need to acquire the lock.
            if (!semLock)
            {
                await playerLock.WaitAsync();

                players.Remove(guildId);

                playerLock.Release();
            }
            else
            {
                players.Remove(guildId);
            }
        }

        private void ConnectWebSocket()
        {
            // Continuously attempt connections to Lavalink
            Task.Run(async () =>
            {
                while (!webSocket.IsConnected())
                {
                    if (lavalinkCancellation.IsCancellationRequested)
                        break;

                    try
                    {
                        await webSocket.Connect();
                    }
                    catch (Exception ex)
                    {
                        logger.Log("An exception occured while opening the WebSocket connection", LogSeverity.Debug, ex);
                    }
                    finally
                    {
                        if (!webSocket.IsConnected())
                        {
                            // Please no really long connection waits :(
                            if (connectionWait < 30000)
                                connectionWait = connectionWait + 3000;
                            logger.Log($"Failed to connect to Lavalink node at {webSocket.GetHostUri()}", LogSeverity.Warning);
                            logger.Log($"Waiting {connectionWait / 1000} seconds before reconnecting", LogSeverity.Warning);
                        }
                    }

                    await Task.Delay(connectionWait);
                }
            });
        }

        /// <summary>
        /// Starts the lavalink connection
        /// </summary>
        /// <returns></returns>
        public Task StartAsync()
        {
            return Task.Run(() =>
            {
                if (baseDiscordClient.CurrentUser == null)
                    throw new InvalidOperationException("Can't connect when CurrentUser is null. Please wait until Discord connects.");

                lavalinkCancellation = new CancellationTokenSource();

                // Setup the lavalink websocket connection
                webSocket = new LavalinkWebSocket(this, config);

                webSocket.OnReceive += (message) =>
                {
                    // TODO: Implement stats event
                    switch((string)message["op"])
                    {
                        case "playerUpdate":
                            {
                                logger.Log("Received Dispatch (PLAYER_UPDATE)", LogSeverity.Debug);

                                ulong guildId = (ulong)message["guildId"];

                                if (players.TryGetValue(guildId, out LavalinkPlayer player))
                                {
                                    LavalinkTrack currentTrack = player.CurrentTrack;

                                    player.FireEvent(Event.PlayerUpdate, message["state"]["position"]);
                                    PlayerUpdate?.InvokeAsync(player, currentTrack, (long)message["state"]["position"]);
                                }

                                break;
                            }

                        case "event":
                            {
                                ulong guildId = (ulong)message["guildId"];

                                if (players.TryGetValue(guildId, out LavalinkPlayer player))
                                {
                                    LavalinkTrack currentTrack = player.CurrentTrack;

                                    switch ((string)message["type"])
                                    {
                                        case "TrackEndEvent":
                                            {
                                                logger.Log("Received Dispatch (TRACK_END_EVENT)", LogSeverity.Debug);
                                                
                                                player.FireEvent(Event.TrackEnd, message["reason"]);
                                                TrackEnd?.InvokeAsync(player, currentTrack, (string)message["reason"]);

                                                break;
                                            }

                                        case "TrackExceptionEvent":
                                            {
                                                logger.Log("Received Dispatch (TRACK_EXCEPTION_EVENT)", LogSeverity.Debug);

                                                player.FireEvent(Event.TrackException, message["error"]);
                                                TrackException?.InvokeAsync(player, currentTrack, (string)message["error"]);

                                                break;
                                            }

                                        case "TrackStuckEvent":
                                            {
                                                logger.Log("Received Dispatch (TRACK_STUCK_EVENT)", LogSeverity.Debug);

                                                player.FireEvent(Event.TrackStuck, message["thresholdMs"]);
                                                TrackStuck?.InvokeAsync(player, currentTrack, (long)message["thresholdMs"]);

                                                break;
                                            }

                                        default:
                                            {
                                                logger.Log($"Received Unknown Event Type {(string)message["type"]}", LogSeverity.Debug);

                                                break;
                                            }
                                    }
                                }

                                break;
                            }

                        case "stats":
                            {
                                Stats?.InvokeAsync(new LavalinkStats(message));

                                break;
                            }

                        default:
                            {
                                logger.Log($"Received Uknown Dispatch ({(string)message["op"]})", LogSeverity.Debug);

                                break;
                            }
                    }

                    return Task.CompletedTask;
                };

                webSocket.OnClosed += async (closeStatus, closeDescription) =>
                {
                    await playerLock.WaitAsync();

                    players.Clear();
                    ConnectWebSocket();

                    playerLock.Release();
                };

                ConnectWebSocket();
            });
        }

        /// <summary>
        /// Stops the lavalink connection
        /// </summary>
        /// <returns></returns>
        public Task StopAsync()
        {
            return Task.Run(async () =>
            {
                lavalinkCancellation.Cancel();
                await webSocket.Disconnect();
            });
        }

        /// <summary>
        /// Joins a voice channel and returns a new instance of <see cref="LavalinkPlayer"/>
        /// </summary>
        /// <param name="voiceChannel"></param>
        /// <returns></returns>
        public async Task<LavalinkPlayer> JoinAsync(IVoiceChannel voiceChannel)
        {
            if (players.ContainsKey(voiceChannel.GuildId))
                throw new InvalidOperationException("This guild is already actively connected");

            LavalinkPlayer player = new LavalinkPlayer(this, voiceChannel);
            players.Add(voiceChannel.GuildId, player);

            // Initiates the voice connection
            await player.ConnectAsync();

            return player;
        }

        /// <summary>
        /// Gets a currently active <see cref="LavalinkPlayer"/> for the guild id
        /// </summary>
        /// <param name="guildId"></param>
        /// <returns></returns>
        public LavalinkPlayer GetPlayer(ulong guildId)
        {
            players.TryGetValue(guildId, out LavalinkPlayer player);
            return player;
        }

        /// <summary>
        /// Leaves a voice channel
        /// </summary>
        /// <param name="guildId"></param>
        public async Task LeaveAsync(ulong guildId)
        {
            if (!players.TryGetValue(guildId, out LavalinkPlayer player))
                throw new InvalidOperationException("This guild is not actively connected");

            await player.DisconnectAsync();
        }

        private async Task<JToken> RequestLoadTracksAsync(string identifier)
        {
            DateTime requestTime = DateTime.UtcNow;
            string response = await client.GetStringAsync($"http://{config.RESTHost}:{config.RESTPort}/loadtracks?identifier={identifier}");
            logger.Log($"GET loadtracks: {(DateTime.UtcNow - requestTime).TotalMilliseconds} ms", LogSeverity.Verbose);

            JToken json = JToken.Parse(response);
            return json;
        }

        /// <summary>
        /// Gets a track from a track id
        /// </summary>
        /// <param name="trackId"></param>
        /// <remarks>Some properties may be missing from the track</remarks>
        /// <returns></returns>
        public LavalinkTrack GetTrackFromId(string trackId)
        {
            #region BYTE_REF
            /*
                Thank you sedmelluq for this info

                all integers are big-endian.
                text is 2-byte integer for length of bytes + utf8 bytes

                4 bytes size + flags (2 highest order bits are flags, the rest is size)
                1 byte of message version
                title (text)
                author (text)
                length (8-byte integer)
                identifier (text)
                1 byte boolean of whether it is a stream
                1 byte boolean of whether url field is present
                url (text)
                source name (text)
                if source is http or local, then: container type (text)
                position in ms (8-byte integer). always 0 for tracks provided by lavalink
             */
            #endregion

            try
            {
                byte[] trackBytes = Convert.FromBase64String(trackId);
                int offset = 5;

                // Skipping size, flags, and message version at the moment

                ushort titleSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                string title = Encoding.UTF8.GetString(trackBytes, offset + 2, titleSize);
                offset += 2 + titleSize;

                ushort authorSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                string author = Encoding.UTF8.GetString(trackBytes, offset + 2, authorSize);
                offset += 2 + authorSize;

                ulong length = Util.SwapEndianess(BitConverter.ToUInt64(trackBytes, offset));
                offset += 8;

                ushort identifierSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                string identifier = Encoding.UTF8.GetString(trackBytes, offset + 2, identifierSize);
                offset += 2 + identifierSize;

                bool stream = BitConverter.ToBoolean(trackBytes, offset);
                offset += 1;

                bool urlPresent = BitConverter.ToBoolean(trackBytes, offset);
                offset += 1;

                string url = null;
                if (urlPresent)
                {
                    ushort urlSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                    url = Encoding.UTF8.GetString(trackBytes, offset + 2, urlSize);
                    offset += 2 + urlSize;
                }

                // Source name is not used in SharpLink outside of this method
                ushort sourceNameSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                string sourceName = Encoding.UTF8.GetString(trackBytes, offset + 2, sourceNameSize);
                offset += 2 + sourceNameSize;

                if (sourceName == "local" || sourceName == "http")
                {
                    // This is ignored and not used but instead we skip the bytes
                    ushort containerTypeSize = Util.SwapEndianess(BitConverter.ToUInt16(trackBytes, offset));
                    offset += 2 + containerTypeSize;
                }

                ulong position = Util.SwapEndianess(BitConverter.ToUInt64(trackBytes, offset));

                return new LavalinkTrack(trackId, title, author, length, identifier, stream, url, position);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // This exception is thrown when the bytes are parsed invalidly and don't have the proper format
                throw new ArgumentException("TrackId failed to parse", ex);
            }
            catch (Exception ex)
            {
                // Any other error is likely an invalid track id
                throw new ArgumentException("TrackId is not valid", ex);
            }
        }

        /// <summary>
        /// Gets multiple track from the Lavalink REST API
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns></returns>
        public async Task<LoadTracksResponse> GetTracksAsync(string identifier)
        {
            JToken json = await RequestLoadTracksAsync(identifier);
            return new LoadTracksResponse(json);
        }
    }
}
