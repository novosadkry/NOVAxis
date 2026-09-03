using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Database
{
    /// <summary>
    /// Puts the schema in place before anything asks it for a row.
    ///
    /// <see cref="DatabaseFacade.EnsureCreatedAsync"/> rather than a migration, because
    /// this context has no deployment history to migrate from - nothing ever registered
    /// it, so no database it describes has ever existed. The moment one does and the
    /// schema changes under it, this has to become migrations: EnsureCreated will not
    /// touch a database which is already there.
    /// </summary>
    public class DatabaseHostService : IHostedService
    {
        private IServiceScopeFactory Scopes { get; }
        private IOptions<DatabaseOptions> Options { get; }
        private ILogger<DatabaseHostService> Logger { get; }

        public DatabaseHostService(
            IServiceScopeFactory scopes,
            IOptions<DatabaseOptions> options,
            ILogger<DatabaseHostService> logger)
        {
            Scopes = scopes;
            Options = options;
            Logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = Scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var created = await context.Database.EnsureCreatedAsync(cancellationToken);

            if (!Options.Value.Active)
            {
                // Worth saying out loud - everything saved will look saved right up until
                // the process ends, which is exactly when somebody finds out otherwise
                Logger.Warning("No database is configured, so anything stored - playlists " +
                               "included - lives only as long as this process");
                return;
            }

            Logger.Info(created
                ? "Database schema created"
                : "Database already present");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
