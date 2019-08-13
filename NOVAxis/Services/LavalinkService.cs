using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

using SharpLink;

namespace NOVAxis.Services
{
    internal static class LavalinkService
    {
        private static LavalinkManager _manager;
        public static LavalinkManager Manager
        {
            get
            {
                return _manager;
            }

            set
            {
                value.Stats += Manager_Stats;
                _manager = value;
            }
        }

        public static SharpLink.Stats.LavalinkStats ManagerStats { get; private set; }
        
        private static Task Manager_Stats(SharpLink.Stats.LavalinkStats stats)
        {
            ManagerStats = stats;
            return Task.CompletedTask;
        }

        public static bool IsConnected => ManagerStats != null;
    }
}
