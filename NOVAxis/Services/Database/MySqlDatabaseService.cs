using System;
using System.Data.Common;
using System.Threading.Tasks;
using Discord;
using MySql.Data.MySqlClient;

namespace NOVAxis.Services.Database
{
    public class MySqlDatabaseService : DatabaseService
    {
        public MySqlDatabaseService(ProgramConfig.DatabaseObject config) : base(config) { }

        protected override string ConnectionString
        {
            get => string.Format("Server={0};Port={1};Database={2};Uid={3};Pwd={4}",
                Config.DbHost,
                Config.DbPort,
                Config.DbName,
                Config.DbUsername,
                Config.DbPassword);
        }

        public override async Task<DbDataReader> Get(string query, params Tuple<string, object>[] arg)
        {
            await LogAsync(new LogMessage(
                LogSeverity.Debug,
                "Database",
                "Query executed"));

            await using var sqlc = new MySqlConnection(ConnectionString);
            await sqlc.OpenAsync();

            var command = sqlc.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(TuplesToMySqlParameters(arg));

            return await command.ExecuteReaderAsync();
        }

        public override async Task Execute(string query, params Tuple<string, object>[] arg)
        {
            await LogAsync(new LogMessage(
                LogSeverity.Debug,
                "Database",
                "Query executed"));

            await using var sqlc = new MySqlConnection(ConnectionString);
            await sqlc.OpenAsync();

            var command = sqlc.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(TuplesToMySqlParameters(arg));

            await command.ExecuteNonQueryAsync();
        }

        public override async Task Setup()
        {
            await Execute(
                "CREATE TABLE IF NOT EXISTS `guilds` (" +
                "`Id` bigint(20) UNSIGNED NOT NULL PRIMARY KEY," +
                "`Prefix` varchar(10) DEFAULT NULL," +
                "`DjRole` bigint(20) UNSIGNED NOT NULL)");
        }

        private static MySqlParameter[] TuplesToMySqlParameters(params Tuple<string, object>[] tuples)
        {
            MySqlParameter[] mySqlParameters = new MySqlParameter[tuples.Length];

            for (int i = 0; i < tuples.Length; i++)
            {
                var (key, value) = tuples[i];
                mySqlParameters[i] = new MySqlParameter(key, value);
            }

            return mySqlParameters;
        }
    }
}
