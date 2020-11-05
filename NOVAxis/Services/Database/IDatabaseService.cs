using System;
using System.Data.Common;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Database
{
    public interface IDatabaseService
    {
        event Func<LogMessage, Task> LogEvent;
        bool Active { get; }

        Task<DbDataReader> Get(string query, params Tuple<string, object>[] arg);
        Task Execute(string query, params Tuple<string, object>[] arg);
    }
}
