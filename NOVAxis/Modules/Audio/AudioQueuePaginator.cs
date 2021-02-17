using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;

using Interactivity;
using Interactivity.Pagination;

namespace NOVAxis.Modules.Audio
{
    public class AudioQueuePaginator
    {
        private readonly int _tracksPerPage;
            
        public AudioQueuePaginator(int tracksPerPage)
        {
            _tracksPerPage = tracksPerPage;

            Header = new List<EmbedFieldBuilder>();
            Tracks = new List<EmbedFieldBuilder>();
            Footer = new List<EmbedFieldBuilder>();
        }

        public int MaxPageIndex { get; private set; }
        public List<EmbedFieldBuilder> Header { get; }
        public List<EmbedFieldBuilder> Tracks { get; }
        public List<EmbedFieldBuilder> Footer { get; }

        private Task<PageBuilder> PageFactory(int pageIndex)
        {
            var page = new PageBuilder();
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

            page.WithColor(System.Drawing.Color.FromArgb(52, 231, 231))
                .WithFields(content);

            return Task.FromResult(page);
        }

        public Paginator Build()
        {
            MaxPageIndex = (int)Math.Floor((float)Tracks.Count / _tracksPerPage);

            return new LazyPaginatorBuilder()
                .WithPageFactory(PageFactory)
                .WithMaxPageIndex(MaxPageIndex)

                .WithCancelledEmbed(new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Ukončeno uživatelem)")
                    .WithTitle("Mé jádro přerušilo čekání na lidský vstup"))

                .WithTimoutedEmbed(new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Vypršel časový limit)")
                    .WithTitle("Mé jádro přerušilo čekání na lidský vstup"))

                .WithFooter(PaginatorFooter.None)
                .WithDefaultEmotes()
                .Build();
        }
    }
}