using System;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Database
{
    public interface IDatabaseService
    {
        event Func<LogMessage, Task> LogEvent;
        bool Active { get; }

        Task<object> GetValue(string query, int index, params Tuple<string, object>[] arg);
        Task<object[]> GetValues(string query, int expected, params Tuple<string, object>[] arg);
        Task Execute(string query, params Tuple<string, object>[] arg);
    }
}
