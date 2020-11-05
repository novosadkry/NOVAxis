using System;
using System.Data.Common;
using System.Threading.Tasks;
using Discord;
using Microsoft.Data.Sqlite;

namespace NOVAxis.Services.Database
{
    class SqliteDatabaseService : DatabaseService
    {
        public SqliteDatabaseService(ProgramConfig.DatabaseObject config) : base(config) { }

        protected override string ConnectionString => $"Data Source={Config.DbName}.db";

        public override async Task<DbDataReader> Get(string query, params Tuple<string, object>[] arg)
        {
            await LogAsync(new LogMessage(
                LogSeverity.Debug,
                "Database",
                "Query executed"));

            await using var sqlc = new SqliteConnection(ConnectionString);
            await sqlc.OpenAsync();

            var command = sqlc.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(TuplesToSqliteParameters(arg));

            return await command.ExecuteReaderAsync();
        }

        public override async Task Execute(string query, params Tuple<string, object>[] arg)
        {
            await LogAsync(new LogMessage(
                LogSeverity.Debug,
                "Database",
                "Query executed"));

            await using var sqlc = new SqliteConnection(ConnectionString);
            await sqlc.OpenAsync();

            var command = sqlc.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddRange(TuplesToSqliteParameters(arg));

            await command.ExecuteNonQueryAsync();
        }

        public override async Task Setup()
        {
            await Execute(
                "CREATE TABLE IF NOT EXISTS `guilds` (" +
                "`Id` INTEGER NOT NULL PRIMARY KEY," +
                "`Prefix` TEXT DEFAULT NULL," +
                "`DjRole` INTEGER NOT NULL)");
        }

        private static SqliteParameter[] TuplesToSqliteParameters(params Tuple<string, object>[] tuples)
        {
            SqliteParameter[] mySqlParameters = new SqliteParameter[tuples.Length];

            for (int i = 0; i < tuples.Length; i++)
            {
                var (key, value) = tuples[i];
                mySqlParameters[i] = new SqliteParameter(key, value);
            }

            return mySqlParameters;
        }
    }
}