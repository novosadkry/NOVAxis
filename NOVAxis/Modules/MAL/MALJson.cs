using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace NOVAxis.Modules.MAL
{
    public static class MALJson
    {
        public const string API = "https://api.jikan.moe/v3/{0}";

        public abstract class MALResult
        {
            public class Info
            {
                public class Genre
                {
                    public int mal_id { get; set; }
                    public string type { get; set; }
                    public string name { get; set; }
                    public string url { get; set; }
                }

                public string title_english { get; set; }
                public List<string> title_synonyms { get; set; }
                public string title_japanese { get; set; }
                public string status { get; set; }
                public string type { get; set; }
                public string rating { get; set; }
                public int? rank { get; set; }
                public List<Genre> genres { get; set; }
            }

            public class Anime : MALResult
            {
                public bool airing { get; set; }
                public string aired => base.published;

                public int episodes { get; set; }
                public string rated { get; set; }

                protected override string api => string.Format(API, "anime/{0}");
            }

            public class Manga : MALResult
            {
                public bool publishing { get; set; }
                public new string published => base.published;

                public int chapters { get; set; }
                public int volumes { get; set; }

                protected override string api => string.Format(API, "manga/{0}");
            }

            protected virtual string published 
                => $"from `{start_date?.ToShortDateString() ?? "?"}`\n" + 
                   $"to `{end_date?.ToShortDateString() ?? "?"}`";

            public virtual async Task<Info> GetInfo()
            {
                string api = string.Format(this.api, mal_id);
                api = Uri.EscapeUriString(api);

                using WebClient client = new WebClient { Encoding = Encoding.UTF8 };

                Task<string> result = client.DownloadStringTaskAsync(api);
                return JObject.Parse(await result).ToObject<Info>();
            }

            protected abstract string api { get; }

            public Info info { get; set; }
            public int mal_id { get; set; }
            public string url { get; set; }
            public string image_url { get; set; }
            public string title { get; set; }
            public string synopsis { get; set; }
            public string type { get; set; }
            public float score { get; set; }
            public DateTime? start_date { get; set; }
            public DateTime? end_date { get; set; }
            public int members { get; set; }
        }

        public static List<T> Get<T>(string json) where T : MALResult
        {
            JObject mainObject = JObject.Parse(json);
            JArray dataArray = (JArray)mainObject["results"];

            return dataArray.ToObject<List<T>>();
        }
    }
}