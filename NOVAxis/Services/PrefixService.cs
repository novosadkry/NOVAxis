using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MySql.Data.MySqlClient;

namespace NOVAxis.Services
{
    class PrefixService
    {
        private static Dictionary<ulong, string> cache = new Dictionary<ulong, string>();

        private DatabaseService db;

        public PrefixService(ProgramConfig config)
        {
            db = new DatabaseService(config);
        }

        public async Task<string> GetPrefix(ulong id)
        {
            if (!cache.ContainsKey(id))
                cache[id] = (string) await db.GetValue("SELECT Prefix FROM Guilds WHERE Id=@id", 
                    new MySqlParameter("id", id));

            return cache[id];
        }

        public async Task SetPrefix(ulong id, string prefix)
        {
            await db.Execute("REPLACE INTO Guilds (Id, Prefix) VALUES (@id, @prefix)",
                new MySqlParameter("id", id),
                new MySqlParameter("prefix", prefix));

            cache[id] = prefix;
        }
    }
}
