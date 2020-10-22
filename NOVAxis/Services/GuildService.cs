using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using MySql.Data.MySqlClient;

namespace NOVAxis.Services
{
    public class GuildService
    {
        public class GuildInfo
        {
            public string Prefix { get; set; }
            public ulong MuteGroup { get; set; }
            public ulong DjGroup { get; set; }
        }

        private Dictionary<ulong, GuildInfo> cache;
        private DatabaseService db;

        public string DefaultPrefix { get; }

        public GuildService()
        {
            db = new DatabaseService();
            cache = new Dictionary<ulong, GuildInfo>();
            
            DefaultPrefix = Program.Config.DefaultPrefix;
        }

        public async Task<GuildInfo> GetInfo(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetInfo(context.Guild.Id);

            return null;
        }

        public async Task<GuildInfo> GetInfo(ulong id)
        {
            if (!cache.TryGetValue(id, out GuildInfo info))
            {
                if (db.Config.Active)
                {
                    var result = await db.GetValues(
                        "SELECT Prefix, MuteGroup, DjGroup FROM Guilds WHERE Id=@id",
                        new MySqlParameter("id", id));

                    info = new GuildInfo
                    {
                        Prefix = (string)result[0],
                        MuteGroup = (ulong)result[1],
                        DjGroup = (ulong)result[2]
                    };
                }

                else
                {
                    info = new GuildInfo
                    {
                        Prefix = DefaultPrefix,
                        MuteGroup = 0,
                        DjGroup = 0
                    };
                }

                cache[id] = info;
            }

            return cache[id];
        }

        public async Task SetInfo(ICommandContext context, GuildInfo info)
        {
            if (context.User.GetType() != typeof(IGuildUser))
                throw new InvalidCastException("Context user is not a type of IGuildUser");

            await SetInfo(context.Guild.Id, info);
        }

        public async Task SetInfo(ulong id, GuildInfo info)
        {
            if (db.Config.Active)
            {
                await db.Execute(
                    "UPDATE Guilds SET Prefix = @prefix, MuteGroup = @muteGroup, DjGroup = @djGroup WHERE Id=@id",
                    new MySqlParameter("id", id),
                    new MySqlParameter("prefix", info.Prefix),
                    new MySqlParameter("muteGroup", info.MuteGroup),
                    new MySqlParameter("djGroup", info.DjGroup));
            }

            cache[id] = info;
        }

        public async Task<string> GetPrefix(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetPrefix(context.Guild.Id);

            return DefaultPrefix;
        }

        public async Task<string> GetPrefix(ulong id)
        {
            return (await GetInfo(id)).Prefix;
        }

        public async Task SetPrefix(ICommandContext context, string prefix)
        {
            if (context.User.GetType() != typeof(IGuildUser))
                throw new InvalidCastException("Context user is not a type of IGuildUser");

            await SetPrefix(context.Guild.Id, prefix);
        }

        public async Task SetPrefix(ulong id, string prefix)
        {
            GuildInfo info = await GetInfo(id);
            info.Prefix = prefix;

            await SetInfo(id, info);
        }
    }
}
