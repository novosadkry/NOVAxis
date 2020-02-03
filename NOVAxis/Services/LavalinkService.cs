using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpLink;
using SharpLink.Stats;

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

        public static LavalinkStats ManagerStats { get; private set; }
        
        private static Task Manager_Stats(LavalinkStats stats)
        {
            ManagerStats = stats;
            return Task.CompletedTask;
        }

        public static bool IsConnected => ManagerStats != null;
    }
}
