using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NOVAxis.Database;
using NOVAxis.Database.Entities;

namespace NOVAxis.Services.CS2
{
    public class CS2DemoQueueService
    {
        private readonly ProgramDbContext _dbContext;

        public CS2DemoQueueService(ProgramDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<CS2DemoQueue> DequeueAsync()
        {
            var message = await _dbContext.CS2DemoQueue
                .OrderBy(m => m.QueuedAt)
                .Where(m => m.Status == CS2DemoQueueStatus.Pending)
                .FirstOrDefaultAsync();

            if (message == null) return null;

            message.Status = CS2DemoQueueStatus.Processing;
            await _dbContext.SaveChangesAsync();

            return message;
        }

        public async Task UpdateStatusAsync(CS2DemoQueue message)
        {
            _dbContext.CS2DemoQueue.Update(message);
            await _dbContext.SaveChangesAsync();
        }

        public async Task EnqueueAsync(string demoUrl)
        {
            var message = new CS2DemoQueue { DemoUrl = demoUrl };
            await _dbContext.CS2DemoQueue.AddAsync(message);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<bool> HasPendingDemoAsync(string demoUrl)
        {
            return await _dbContext.CS2DemoQueue
                .AnyAsync(m => m.DemoUrl == demoUrl && m.Status == CS2DemoQueueStatus.Pending);
        }
    }
}
