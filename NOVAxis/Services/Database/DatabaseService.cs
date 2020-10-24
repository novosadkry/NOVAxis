using System;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Database
{
    public abstract class DatabaseService : IDatabaseService
    {
        public event Func<LogMessage, Task> LogEvent;

        protected abstract string ConnectionString { get; }

        protected DatabaseService(ProgramConfig.DatabaseObject config) { Config = config; }

        protected ProgramConfig.DatabaseObject Config { get; }
        public bool Active => Config.Active;

        public abstract Task<object> GetValue(string query, int index, params Tuple<string, object>[] arg);
        public abstract Task<object[]> GetValues(string query, int expected, params Tuple<string, object>[] arg);
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
