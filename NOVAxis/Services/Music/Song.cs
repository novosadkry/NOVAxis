using Discord;

namespace NOVAxis.Services.Music
{
    public class Song
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public IUser RequestedBy { get; set; }

        public Song(string url, string title, IUser requestedBy)
        {
            Url = url;
            Title = title;
            RequestedBy = requestedBy;
        }
    }
}
