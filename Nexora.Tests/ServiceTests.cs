using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Core.Interfaces;
using Nexora.Core.Models;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Services;
using Xunit;

namespace Nexora.Tests;

public class ServiceTests
{
    private static (INexoraService Service, SqliteConnection Connection, ServiceProvider Provider) CreateService()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<NexoraDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<INexoraService, NexoraService>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<INexoraService>(), connection, provider);
    }

    private static (INexoraService Service, ServiceProvider Provider) CreateFileService(string path)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<NexoraDbContext>(options => options.UseSqlite($"Data Source={path};Default Timeout=10;Pooling=False"));
        services.AddSingleton<INexoraService, NexoraService>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<INexoraService>(), provider);
    }

    [Fact]
    public async Task Persists_computers_tariffs_and_active_sessions()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("PC-01");
            var tariff = await service.AddTariffAsync("Hora", 12.50m);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id);
            Assert.Single(await service.GetComputersAsync());
            Assert.Equal(session.Id, (await service.GetActiveSessionAsync(computer.Id))!.Id);
            Assert.Single(await service.GetTariffsAsync());
        }
    }

    [Fact]
    public async Task Rejects_duplicate_names_and_duplicate_active_sessions()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("Cabina");
            var tariff = await service.AddTariffAsync("Hora", 10);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddComputerAsync("cabina"));
            await service.StartSessionAsync(computer.Id, tariff.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartSessionAsync(computer.Id, tariff.Id));
        }
    }

    [Fact]
    public async Task Finishing_updates_state_history_and_revenue()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("PC-02");
            var tariff = await service.AddTariffAsync("Hora", 60);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id);
            var finished = await service.FinishSessionAsync(session.Id);
            Assert.NotNull(finished.EndedAtUtc);
            Assert.Equal(0, (await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local)).ActiveSessions);
            Assert.Equal(finished.Amount, (await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local)).TodayRevenue);
            Assert.Single(await service.GetRecentSessionsAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinishSessionAsync(session.Id));
        }
    }

    [Fact]
    public async Task Concurrent_starts_allow_only_one_active_session()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            var first = CreateFileService(path);
            var second = CreateFileService(path);
            using (first.Provider)
            using (second.Provider)
            {
                await first.Service.InitializeAsync();
                var computer = await first.Service.AddComputerAsync("CONCURRENT");
                var tariff = await first.Service.AddTariffAsync("Hora", 10m);

                var attempts = await Task.WhenAll(
                    CaptureStartAsync(first.Service, computer.Id, tariff.Id),
                    CaptureStartAsync(second.Service, computer.Id, tariff.Id));

                Assert.Single(attempts, x => x.Success);
                Assert.Single(attempts, x => !x.Success);
                Assert.NotNull(await first.Service.GetActiveSessionAsync(computer.Id));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}-shm")) File.Delete($"{path}-shm");
            if (File.Exists($"{path}-wal")) File.Delete($"{path}-wal");
        }
    }

    private static async Task<(bool Success, Exception? Error)> CaptureStartAsync(INexoraService service, Guid computerId, Guid tariffId)
    {
        try
        {
            await service.StartSessionAsync(computerId, tariffId);
            return (true, null);
        }
        catch (Exception error)
        {
            return (false, error);
        }
    }

    [Fact]
    public async Task Active_session_is_recovered_after_service_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-restart-{Guid.NewGuid():N}.db");
        try
        {
            Guid computerId;
            using (var first = CreateFileService(path).Provider)
            {
                var service = first.GetRequiredService<INexoraService>();
                await service.InitializeAsync();
                var computer = await service.AddComputerAsync("RESTART");
                var tariff = await service.AddTariffAsync("Hora", 10m);
                computerId = computer.Id;
                await service.StartSessionAsync(computer.Id, tariff.Id);
            }

            using (var second = CreateFileService(path).Provider)
            {
                var service = second.GetRequiredService<INexoraService>();
                await service.InitializeAsync();
                var active = await service.GetActiveSessionAsync(computerId);
                Assert.NotNull(active);
                Assert.Null(active.EndedAtUtc);
                Assert.True(active.Duration >= TimeSpan.Zero);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}-shm")) File.Delete($"{path}-shm");
            if (File.Exists($"{path}-wal")) File.Delete($"{path}-wal");
        }
    }

    [Fact]
    public async Task Active_sessions_are_not_counted_as_daily_revenue()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("ACTIVE-REVENUE");
            var tariff = await service.AddTariffAsync("Hora", 60m);
            await service.StartSessionAsync(computer.Id, tariff.Id);

            var dashboard = await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local);

            Assert.Equal(1, dashboard.ActiveSessions);
            Assert.Equal(0m, dashboard.TodayRevenue);
        }
    }

    [Fact]
    public async Task Migration_preserves_data_from_an_EnsureCreated_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NexoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            await using (var legacy = new NexoraDbContext(options))
            {
                await legacy.Database.EnsureCreatedAsync();
                legacy.Computers.Add(new Computer("LEGACY"));
                await legacy.SaveChangesAsync();
            }

            var current = CreateFileService(path);
            using (current.Provider)
            {
                await current.Service.InitializeAsync();
                Assert.Contains(await current.Service.GetComputersAsync(), x => x.Name == "LEGACY");
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}-shm")) File.Delete($"{path}-shm");
            if (File.Exists($"{path}-wal")) File.Delete($"{path}-wal");
        }
    }
}
