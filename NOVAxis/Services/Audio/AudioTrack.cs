using NOVAxis.Extensions;

using Discord;
using Victoria;

namespace NOVAxis.Services.Audio
{
    public class AudioTrack : LavaTrack
    {
        public AudioTrack(LavaTrack lavaTrack)
            : base(lavaTrack) { }

        public IUser RequestedBy { get; set; }
        public string ThumbnailUrl => this.GetThumbnailUrl();
    }
}