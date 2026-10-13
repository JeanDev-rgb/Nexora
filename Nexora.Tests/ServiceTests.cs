using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Core.Enums;
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
            Assert.Equal(0m, (await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local)).TodayRevenue);
            Assert.Single(await service.GetRecentSessionsAsync());
            await using (var verification = new NexoraDbContext(new DbContextOptionsBuilder<NexoraDbContext>().UseSqlite(connection).Options))
                Assert.Equal(0, await verification.Charges.CountAsync(x => x.SessionId == session.Id));
            await service.RegisterPaymentAsync(session.Id);
            Assert.Equal(finished.Amount, (await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local)).TodayRevenue);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterPaymentAsync(session.Id));
            await using (var verification = new NexoraDbContext(new DbContextOptionsBuilder<NexoraDbContext>().UseSqlite(connection).Options))
                Assert.Equal(1, await verification.Charges.CountAsync(x => x.SessionId == session.Id));
        }
    }

    [Fact]
    public async Task Concurrent_finish_operations_create_one_charge()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-charge-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            var first = CreateFileService(path);
            var second = CreateFileService(path);
            using (first.Provider)
            using (second.Provider)
            {
                await first.Service.InitializeAsync();
                var computer = await first.Service.AddComputerAsync("CHARGE-CONCURRENT");
                var tariff = await first.Service.AddTariffAsync("Hora", 60m);
                var session = await first.Service.StartSessionAsync(computer.Id, tariff.Id);

                var attempts = await Task.WhenAll(
                    CaptureFinishAsync(first.Service, session.Id),
                    CaptureFinishAsync(second.Service, session.Id));

                Assert.Single(attempts, x => x.Success);
                Assert.Single(attempts, x => !x.Success);
                await first.Service.RegisterPaymentAsync(session.Id);
                await using var verification = new NexoraDbContext(new DbContextOptionsBuilder<NexoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
                Assert.Equal(1, await verification.Charges.CountAsync(x => x.SessionId == session.Id));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}-shm")) File.Delete($"{path}-shm");
            if (File.Exists($"{path}-wal")) File.Delete($"{path}-wal");
        }
    }

    private static async Task<(bool Success, Exception? Error)> CaptureFinishAsync(INexoraService service, Guid sessionId)
    {
        try
        {
            await service.FinishSessionAsync(sessionId);
            return (true, null);
        }
        catch (Exception error)
        {
            return (false, error);
        }
    }

    [Fact]
    public async Task Registered_revenue_survives_service_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-revenue-restart-{Guid.NewGuid():N}.db");
        try
        {
            decimal chargedAmount;
            using (var first = CreateFileService(path).Provider)
            {
                var service = first.GetRequiredService<INexoraService>();
                await service.InitializeAsync();
                var computer = await service.AddComputerAsync("REVENUE-RESTART");
                var tariff = await service.AddTariffAsync("Hora", 60m);
                chargedAmount = (await service.FinishSessionAsync((await service.StartSessionAsync(computer.Id, tariff.Id)).Id)).Amount;
            }

            using (var second = CreateFileService(path).Provider)
            {
                var service = second.GetRequiredService<INexoraService>();
                await service.InitializeAsync();
                await service.RegisterPaymentAsync((await service.GetRecentSessionsAsync()).Single().Id);
                var dashboard = await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local);
                Assert.Equal(chargedAmount, dashboard.TodayRevenue);
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
    public async Task Dashboard_exposes_operational_counts_and_pending_debt()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var available = await service.AddComputerAsync("AVAILABLE");
            var busy = await service.AddComputerAsync("BUSY");
            var offline = await service.AddComputerAsync("OFFLINE");
            await service.SetComputerOutOfServiceAsync(offline.Id);
            var tariff = await service.AddTariffAsync("Dashboard", 60m);
            await service.StartSessionAsync(busy.Id, tariff.Id);
            var finished = await service.FinishSessionAsync((await service.GetActiveSessionAsync(busy.Id))!.Id);

            var dashboard = await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local);

            Assert.Equal(3, dashboard.TotalComputers);
            Assert.Equal(2, dashboard.AvailableComputers);
            Assert.Equal(1, dashboard.OutOfServiceComputers);
            Assert.Equal(1, dashboard.PendingPayments);
            Assert.Equal(0m, dashboard.TodayRevenue);
            Assert.True(finished.IsPending);
        }
    }

    [Fact]
    public async Task Prepaid_session_uses_fixed_amount_and_can_be_paid_at_start()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("PREPAID");
            var tariff = await service.AddTariffAsync("Paquete", 60m);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id, SessionMode.Prepaid, TimeSpan.FromMinutes(30), true);

            Assert.Equal(SessionMode.Prepaid, session.Mode);
            Assert.Equal(30m, session.Amount);
            Assert.False(session.IsEstimated);
            Assert.Equal(PaymentStatus.Paid, session.PaymentStatus);
            Assert.Equal(30m, (await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local)).TodayRevenue);
        }
    }

    [Fact]
    public async Task Paid_active_prepaid_session_appears_in_cash_not_estimation_or_debt()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("PAID-PREPAID-CASH");
            var tariff = await service.AddTariffAsync("Paquete caja", 60m);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id,
                SessionMode.Prepaid, TimeSpan.FromMinutes(30), payAtStart: true);

            var dashboard = await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local);
            var cashEntry = Assert.Single(await service.GetCashEntriesAsync());

            Assert.Equal(1, dashboard.ActiveSessions);
            Assert.Equal(0m, dashboard.ActiveEstimatedAmount);
            Assert.Equal(0, dashboard.PendingPayments);
            Assert.Equal(0m, dashboard.PendingAmount);
            Assert.Equal(session.Amount, dashboard.TodayRevenue);
            Assert.Equal(PaymentStatus.Paid, cashEntry.PaymentStatus);
            Assert.NotNull(cashEntry.ChargedAtUtc);
            Assert.Equal(DateTimeKind.Utc, cashEntry.ChargedAtUtc!.Value.Kind);
        }
    }

    [Fact]
    public async Task Unpaid_active_prepaid_is_a_fixed_pending_obligation_not_revenue()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("PENDING-PREPAID-CASH");
            var tariff = await service.AddTariffAsync("Paquete pendiente", 60m);
            await service.StartSessionAsync(computer.Id, tariff.Id,
                SessionMode.Prepaid, TimeSpan.FromMinutes(30));

            var dashboard = await service.GetDashboardAsync(DateTime.Now, TimeZoneInfo.Local);
            var cashEntry = Assert.Single(await service.GetCashEntriesAsync());

            Assert.Equal(0m, dashboard.ActiveEstimatedAmount);
            Assert.Equal(1, dashboard.PendingPayments);
            Assert.Equal(30m, dashboard.PendingAmount);
            Assert.Equal(0m, dashboard.TodayRevenue);
            Assert.True(cashEntry.IsPending);
            Assert.Equal(30m, cashEntry.Amount);
        }
    }

    [Fact]
    public async Task Revenue_uses_effective_charge_date_when_session_started_on_previous_day()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("CHARGE-EFFECTIVE-DATE");
            var tariff = await service.AddTariffAsync("Fecha", 60m);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id);
            var finished = await service.FinishSessionAsync(session.Id);
            var yesterdayStartUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
            await using (var verification = new NexoraDbContext(new DbContextOptionsBuilder<NexoraDbContext>().UseSqlite(connection).Options))
                await verification.Database.ExecuteSqlInterpolatedAsync($"UPDATE Sessions SET StartedAt = {yesterdayStartUtc} WHERE Id = {session.Id}");

            var paid = await service.RegisterPaymentAsync(session.Id);
            var chargedLocalDay = DateTime.SpecifyKind(paid.ChargedAtUtc!.Value.ToLocalTime().Date, DateTimeKind.Unspecified);
            var revenueOnChargeDay = await service.GetRevenueAsync(chargedLocalDay, chargedLocalDay.AddDays(1), TimeZoneInfo.Local);

            Assert.NotEqual(paid.StartedAtUtc.ToLocalTime().Date, paid.ChargedAtUtc.Value.ToLocalTime().Date);
            Assert.Equal(finished.Amount, revenueOnChargeDay);
        }
    }

    [Fact]
    public async Task Expired_prepaid_session_stays_in_use_until_admin_releases_it()
    {
        var (service, connection, provider) = CreateService();
        using (provider)
        await using (connection)
        {
            await service.InitializeAsync();
            var computer = await service.AddComputerAsync("EXPIRED-PACKAGE");
            var tariff = await service.AddTariffAsync("Paquete", 60m);
            var session = await service.StartSessionAsync(computer.Id, tariff.Id, SessionMode.Prepaid, TimeSpan.FromSeconds(1));
            await Task.Delay(1200);

            var expired = await service.GetActiveSessionAsync(computer.Id);
            Assert.NotNull(expired);
            Assert.Equal(SessionState.TimeExpired, expired.State);
            Assert.Equal(TimeSpan.FromSeconds(1), expired.Duration);
            await service.FinishSessionAsync(session.Id);
            Assert.Null(await service.GetActiveSessionAsync(computer.Id));
        }
    }

    [Fact]
    public async Task Migration_preserves_data_from_a_real_EnsureCreated_legacy_schema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexora-upgrade-{Guid.NewGuid():N}.db");
        var activeComputerId = Guid.Empty;
        try
        {
            var options = new DbContextOptionsBuilder<LegacyNexoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            await using (var legacy = new LegacyNexoraDbContext(options))
            {
                Assert.True(await legacy.Database.EnsureCreatedAsync());
                var computer = new Computer("LEGACY");
                var tariff = new Tariff("Legacy", 60m);
                var started = DateTime.UtcNow.AddMinutes(-2);
                var completed = new Session(computer.Id, tariff, started);
                completed.Finish(started.AddMinutes(1));
                var activeComputer = new Computer("LEGACY-ACTIVE");
                activeComputer.StartSession();
                var active = new Session(activeComputer.Id, tariff, DateTime.UtcNow.AddMinutes(-1));
                activeComputerId = activeComputer.Id;
                legacy.AddRange(computer, activeComputer, tariff, completed, active);
                await legacy.SaveChangesAsync();
            }

            var current = CreateFileService(path);
            using (current.Provider)
            {
                await current.Service.InitializeAsync();
                Assert.Contains(await current.Service.GetComputersAsync(), x => x.Name == "LEGACY");
                Assert.Equal(1m, await current.Service.GetRevenueAsync(DateTime.Today, DateTime.Today.AddDays(1), TimeZoneInfo.Local));
                var recoveredActive = await current.Service.GetActiveSessionsAsync();
                Assert.Contains(activeComputerId, recoveredActive.Keys);
                Assert.Single(await current.Service.GetRecentSessionsAsync());
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}-shm")) File.Delete($"{path}-shm");
            if (File.Exists($"{path}-wal")) File.Delete($"{path}-wal");
        }
    }

    private sealed class LegacyNexoraDbContext(DbContextOptions<LegacyNexoraDbContext> options) : DbContext(options)
    {
        public DbSet<Computer> Computers => Set<Computer>();
        public DbSet<Tariff> Tariffs => Set<Tariff>();
        public DbSet<Session> Sessions => Set<Session>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Computer>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(100).UseCollation("NOCASE").IsRequired();
                entity.Property(x => x.Status).HasConversion<string>().IsRequired();
                entity.HasIndex(x => x.Name).IsUnique();
            });
            modelBuilder.Entity<Tariff>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(100).UseCollation("NOCASE").IsRequired();
                entity.Property(x => x.PricePerHour).HasPrecision(18, 2).IsRequired();
                entity.HasIndex(x => x.Name).IsUnique();
            });
            modelBuilder.Entity<Session>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.Mode);
                entity.Ignore(x => x.State);
                entity.Ignore(x => x.PaymentStatus);
                entity.Ignore(x => x.PaidAtUtc);
                entity.Ignore(x => x.ContractedDurationSeconds);
                entity.Ignore(x => x.IsActive);
                entity.Property(x => x.TariffName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.HourlyRate).HasPrecision(18, 2).IsRequired();
                entity.Property(x => x.FinalAmount).HasPrecision(18, 2);
                entity.HasIndex(x => new { x.ComputerId, x.EndedAt })
                    .HasDatabaseName("IX_Sessions_Computer_Active")
                    .HasFilter("EndedAt IS NULL");
                entity.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
