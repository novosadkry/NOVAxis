using System;
using System.Data.Common;
using System.Threading.Tasks;

using NOVAxis.Core;

using Discord;

namespace NOVAxis.Services.Database
{
    public abstract class DatabaseService : IDatabaseService
    {
        public event Func<LogMessage, Task> LogEvent;
        protected Task LogAsync(LogMessage message) => LogEvent?.Invoke(message);

        protected abstract string ConnectionString { get; }

        protected DatabaseService(ProgramConfig.DatabaseObject config) { Config = config; }

        protected ProgramConfig.DatabaseObject Config { get; }
        public bool Active => Config.Active;

        public abstract Task<DbDataReader> Get(string query, params Tuple<string, object>[] arg);
        public abstract Task Execute(string query, params Tuple<string, object>[] arg);

        public abstract Task Setup();

        public static DatabaseService GetService(ProgramConfig.DatabaseObject config)
        {
            switch (config.DbType)
            {
                case "mysql": 
                    return new MySqlDatabaseService(config);
                case "sqlite": 
                    return new SqliteDatabaseService(config);
            }

            return null;
        }
    }
}
