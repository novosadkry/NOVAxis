using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using MySql.Data.MySqlClient;

namespace NOVAxis.Services
{
    public class PrefixService
    {
        public static Dictionary<ulong, string> Cache { get; } = new Dictionary<ulong, string>();

        public string DefaultPrefix { get; }

        private DatabaseService db;

        public PrefixService()
        {
            db = new DatabaseService();
            DefaultPrefix = Program.Config.DefaultPrefix;
        }

        public async Task<string> GetPrefix(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetPrefix(context.Guild.Id);

            return DefaultPrefix;
        }

        public async Task<string> GetPrefix(ulong id)
        {
            if (!Cache.ContainsKey(id))
            {
                Cache[id] = db.Config.Active
                    ? Cache[id] = (string) await db.GetValue(
                        "SELECT Prefix FROM Guilds WHERE Id=@id", 
                        new MySqlParameter("id", id))
                    : DefaultPrefix;
            }

            return Cache[id];
        }

        public async Task SetPrefix(ICommandContext context, string prefix)
        {
            if (context.User.GetType() != typeof(IGuildUser))
                throw new InvalidCastException("Context user is not a type of IGuildUser");

            await SetPrefix(context.Guild.Id, prefix);
        }

        public async Task SetPrefix(ulong id, string prefix)
        {
            if (db.Config.Active)
            {
                await db.Execute("REPLACE INTO Guilds (Id, Prefix) VALUES (@id, @prefix)", 
                    new MySqlParameter("id", id),
                    new MySqlParameter("prefix", prefix));
            }

            Cache[id] = prefix;
        }
    }
}
