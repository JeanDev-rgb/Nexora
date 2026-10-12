using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nexora.Infrastructure.Persistence;

public sealed class NexoraDbContextFactory : IDesignTimeDbContextFactory<NexoraDbContext>
{
    public NexoraDbContext CreateDbContext(string[] args) =>
        new(NexoraDatabase.CreateOptions());
}
