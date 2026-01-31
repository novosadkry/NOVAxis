using System;

namespace NOVAxis.Database.Entities
{
    public enum CS2DemoQueueStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    public class CS2DemoQueue
    {
        public int Id { get; set; }
        public string DemoUrl { get; set; }
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
        public CS2DemoQueueStatus Status { get; set; } = CS2DemoQueueStatus.Pending;
    }
}
