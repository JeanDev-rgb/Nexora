using Microsoft.EntityFrameworkCore;

namespace Nexora.Infrastructure.Persistence;

public static class NexoraDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nexora", "nexora.db");

    public static DbContextOptions<NexoraDbContext> CreateOptions(string? databasePath = null)
    {
        var path = databasePath ?? DefaultPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        return new DbContextOptionsBuilder<NexoraDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
    }
}
