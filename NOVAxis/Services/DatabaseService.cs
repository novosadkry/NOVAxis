using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MySql.Data.MySqlClient;

using Discord;

namespace NOVAxis.Services
{
    public class DatabaseService
    {
        public static event Func<LogMessage, Task> LogEvent;

        public ProgramConfig.DatabaseObject Config { get; }

        private string ConnectionString
        {
            get => string.Format("Data Source={0},{1};Initial Catalog={2};User ID={3};Password={4}",
                Config.DbHost,
                Config.DbPort,
                Config.DbName,
                Config.DbUsername,
                Config.DbPassword);
        }

        public DatabaseService()
        {
            Config = Program.Config.Database;
        }

        public async Task<object> GetValue(string query, params MySqlParameter[] arg)
        {
            object result = null;

            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(arg);

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (await reader.ReadAsync())
                        {
                            result = reader.GetValue(0);
                        }
                    }
                }
            }

            return result;
        }

        public async Task<object[]> GetValues(string query, params MySqlParameter[] arg)
        {
            object[] result = null;

            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(arg);

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (await reader.ReadAsync())
                        {
                            reader.GetValues(result);
                        }
                    }
                }
            }

            return result;
        }

        public async Task Execute(string query, params MySqlParameter[] arg)
        {
            using (MySqlConnection sqlc = new MySqlConnection(ConnectionString))
            {
                await sqlc.OpenAsync();

                MySqlCommand command = sqlc.CreateCommand();
                command.CommandText = query;
                command.Parameters.AddRange(arg);

                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
