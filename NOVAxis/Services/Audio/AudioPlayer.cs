﻿using Discord;
using Victoria.Player;
using Victoria.WebSocket;

namespace NOVAxis.Services.Audio
{
    public class AudioPlayer : LavaPlayer<LavaTrack>
    {
        public AudioPlayer(WebSocketClient socketClient, IVoiceChannel voiceChannel, ITextChannel textChannel)
            : base(socketClient, voiceChannel, textChannel) { }
    }
}
