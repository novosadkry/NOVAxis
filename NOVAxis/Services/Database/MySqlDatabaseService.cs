using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace NOVAxis.Services.Database
{
    public class MySqlDatabaseService : DatabaseService
    {
        public MySqlDatabaseService(ProgramConfig.DatabaseObject config) : base(config) { }

        private string ConnectionString
        {
            get => string.Format("Server={0};Port={1};Database={2};Uid={3};Pwd={4}",
                Config.DbHost,
                Config.DbPort,
                Config.DbName,
                Config.DbUsername,
                Config.DbPassword);
        }

        public override async Task<object> GetValue(string query, int index, params Tuple<string, object>[] arg)
        {
            object result = null;

            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(TuplesToMySqlParameters(arg));

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (await reader.ReadAsync())
                            result = reader.GetValue(index);
                    }
                }
            }

            return result;
        }

        public override async Task<object[]> GetValues(string query, int expected, params Tuple<string, object>[] arg)
        {
            object[] result = new object[expected];

            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(TuplesToMySqlParameters(arg));

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (await reader.ReadAsync())
                            reader.GetValues(result);
                    }
                }
            }

            return result;
        }

        public override async Task Execute(string query, params Tuple<string, object>[] arg)
        {
            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(TuplesToMySqlParameters(arg));

                await command.ExecuteNonQueryAsync();
            }
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
