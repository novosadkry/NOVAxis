using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using NOVAxis.Services.Database;

namespace NOVAxis.Services
{
    public class GuildService
    {
        public class GuildInfo
        {
            public string Prefix { get; set; }
            public ulong DjRole { get; set; }

            public static GuildInfo Default => 
                new GuildInfo {Prefix = Program.Config.DefaultPrefix};
        }

        private readonly ConcurrentDictionary<ulong, GuildInfo> _cache;
        private readonly IDatabaseService _db;

        public GuildService(IDatabaseService databaseService)
        {
            _db = databaseService;
            _cache = new ConcurrentDictionary<ulong, GuildInfo>();
        }

        public async Task<GuildInfo> GetInfo(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetInfo(context.Guild.Id);

            return GuildInfo.Default;
        }

        public async Task<GuildInfo> GetInfo(ulong id)
        {
            if (!_cache.TryGetValue(id, out _))
            {
                GuildInfo info = GuildInfo.Default;

                if (_db.Active)
                {
                    var result = await _db.GetValues(
                        "SELECT Prefix, DjRole FROM Guilds WHERE Id=@id",
                        3,
                        new Tuple<string, object>("id", id));

                    info.Prefix = (string)(result?[0] ?? info.Prefix);
                    info.DjRole = Convert.ToUInt64(result?[1]);
                }

                _cache[id] = info;
            }

            return _cache[id];
        }

        public async Task SetInfo(ICommandContext context, GuildInfo info)
        {
            if (context.User is IGuildUser)
                await SetInfo(context.Guild.Id, info);
            else
                throw new InvalidCastException("Context user is not a type of IGuildUser");
        }

        public async Task SetInfo(ulong id, GuildInfo info)
        {
            if (_db.Active)
            {
                await _db.Execute(
                    "REPLACE INTO guilds VALUES(@id, @prefix, @djRole)",
                    new Tuple<string, object>("id", id),
                    new Tuple<string, object>("prefix", info.Prefix),
                    new Tuple<string, object>("djRole", info.DjRole));
            }

            _cache[id] = info;
        }
    }
}
