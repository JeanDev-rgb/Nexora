using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Core.Interfaces;
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
}
