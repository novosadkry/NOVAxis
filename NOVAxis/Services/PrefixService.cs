using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private DatabaseService database;

        public PrefixService()
        {
            database = new DatabaseService();
            DefaultPrefix = Program.Config.DefaultPrefix;
        }

        public async Task<string> GetPrefix(ICommandContext context)
        {
            if (context.User is IGuildUser && database.Config.Active)
                return await GetPrefix(context.Guild.Id);

            else
                return DefaultPrefix;
        }

        public async Task<string> GetPrefix(ulong id)
        {
            if (!database.Config.Active)
                throw new InvalidOperationException("Database service is not active");

            if (!Cache.ContainsKey(id))
                Cache[id] = (string)await database.GetValue("SELECT Prefix FROM Guilds WHERE Id=@id",
                    new MySqlParameter("id", id));

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
            if (!database.Config.Active)
                throw new InvalidOperationException("Database service is not active");

            await database.Execute("REPLACE INTO Guilds (Id, Prefix) VALUES (@id, @prefix)",
                new MySqlParameter("id", id),
                new MySqlParameter("prefix", prefix));

            Cache[id] = prefix;
        }
    }
}
