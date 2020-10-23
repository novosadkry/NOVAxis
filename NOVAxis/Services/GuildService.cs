using System;
using System.Collections.Concurrent;
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
            public ulong MuteRole { get; set; }
            public ulong DjRole { get; set; }

            public static GuildInfo Default => 
                new GuildInfo {Prefix = Program.Config.DefaultPrefix};
        }

        private ConcurrentDictionary<ulong, GuildInfo> cache;
        private DatabaseService db;

        public GuildService()
        {
            db = new DatabaseService();
            cache = new ConcurrentDictionary<ulong, GuildInfo>();
        }

        public async Task<GuildInfo> GetInfo(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetInfo(context.Guild.Id);

            return GuildInfo.Default;
        }

        public async Task<GuildInfo> GetInfo(ulong id)
        {
            if (!cache.TryGetValue(id, out GuildInfo info))
            {
                if (db.Config.Active)
                {
                    var result = await db.GetValues(
                        "SELECT Prefix, MuteRole, DjRole FROM Guilds WHERE Id=@id",
                        new MySqlParameter("id", id));

                    info = new GuildInfo
                    {
                        Prefix = (string)result?[0],
                        MuteRole = (ulong)(result?[1] ?? 0UL),
                        DjRole = (ulong)(result?[2] ?? 0UL)
                    };
                }

                else
                    info = GuildInfo.Default;

                cache[id] = info;
            }

            return cache[id];
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
            if (db.Config.Active)
            {
                await db.Execute(
                    "UPDATE Guilds SET Prefix = @prefix, MuteRole = @muteRole, DjRole = @djRole WHERE Id=@id",
                    new MySqlParameter("id", id),
                    new MySqlParameter("prefix", info.Prefix),
                    new MySqlParameter("muteRole", info.MuteRole),
                    new MySqlParameter("djRole", info.DjRole));
            }

            cache[id] = info;
        }
    }
}
