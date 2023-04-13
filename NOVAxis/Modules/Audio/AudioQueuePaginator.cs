using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

using Discord;

namespace NOVAxis.Modules.Audio
{
    public class AudioQueuePaginator
    {
        private readonly int _tracksPerPage;
            
        public AudioQueuePaginator(int tracksPerPage)
        {
            _tracksPerPage = tracksPerPage;
        }

        public int MaxPageIndex { get; private set; }
        public IReadOnlyList<EmbedFieldBuilder> Header { get; private set; }
        public IReadOnlyList<EmbedFieldBuilder> Tracks { get; private set; }
        public IReadOnlyList<EmbedFieldBuilder> Footer { get; private set; }

        public AudioQueuePaginator WithHeader(IEnumerable<EmbedFieldBuilder> header)
        {
            Header = header.ToImmutableList();
            return this;
        }

        public AudioQueuePaginator WithTracks(IEnumerable<EmbedFieldBuilder> tracks)
        {
            Tracks = tracks.ToImmutableList();
            MaxPageIndex = (int)((Tracks.Count - 1.0f) / _tracksPerPage);
            return this;
        }

        public AudioQueuePaginator WithFooter(IEnumerable<EmbedFieldBuilder> footer)
        {
            Footer = footer.ToImmutableList();
            return this;
        }

        public Embed Build(int pageIndex)
        {
            var page = new EmbedBuilder();
            var content = new List<EmbedFieldBuilder>();

            // Add header to first page
            if (pageIndex == 0)
            {
                page.WithTitle("Právě přehrávám:");
                content.AddRange(Header);
            }

            // Add tracks to page
            content.AddRange(Tracks
                .Skip(pageIndex * _tracksPerPage)
                .Take(_tracksPerPage)
            );

            // Add footer to last page
            if (pageIndex == MaxPageIndex)
                content.AddRange(Footer);

            page.WithColor(52, 231, 231)
                .WithFields(content);

            return page.Build();
        }
    }
}