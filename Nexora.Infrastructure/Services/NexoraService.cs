using System.Data;
using Microsoft.EntityFrameworkCore;
using Nexora.Core.Enums;
using Nexora.Core.Interfaces;
using Nexora.Core.Models;
using Nexora.Infrastructure.Persistence;

namespace Nexora.Infrastructure.Services;

public sealed class NexoraService(IDbContextFactory<NexoraDbContext> contextFactory) : INexoraService
{
    private Task<NexoraDbContext> CreateContextAsync(CancellationToken cancellationToken) => contextFactory.CreateDbContextAsync(cancellationToken);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await BaselineEnsureCreatedDatabaseAsync(db, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BaselineEnsureCreatedDatabaseAsync(NexoraDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Computers', 'Tariffs', 'Sessions')";
        if (Convert.ToInt32(await tablesCommand.ExecuteScalarAsync(cancellationToken)) != 3) return;
        await using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        if (Convert.ToInt32(await historyCommand.ExecuteScalarAsync(cancellationToken)) > 0) return;
        var initialMigration = db.Database.GetMigrations().First();
        await using var baselineCommand = connection.CreateCommand();
        baselineCommand.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($migration, $version);";
        var migrationParameter = baselineCommand.CreateParameter();
        migrationParameter.ParameterName = "$migration";
        migrationParameter.Value = initialMigration;
        baselineCommand.Parameters.Add(migrationParameter);
        var versionParameter = baselineCommand.CreateParameter();
        versionParameter.ParameterName = "$version";
        versionParameter.Value = "10.0.12";
        baselineCommand.Parameters.Add(versionParameter);
        await baselineCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Computer>> GetComputersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        return await db.Computers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tariff>> GetTariffsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        return await db.Tariffs.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var rows = await db.Sessions.AsNoTracking().Where(x => x.EndedAt != null)
            .Join(db.Computers, session => session.ComputerId, computer => computer.Id, (session, computer) => new { session, computer.Name })
            .GroupJoin(db.Charges, row => row.session.Id, charge => charge.SessionId, (row, charges) => new { row.session, row.Name, Charge = charges.SingleOrDefault() })
            .OrderByDescending(x => x.session.EndedAt).Take(count).ToListAsync(cancellationToken);
        return rows.Select(x => ToSummary(x.session, x.Name, x.Charge)).ToList();
    }

    public async Task<SessionSummary?> GetActiveSessionAsync(Guid computerId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.ComputerId == computerId && x.EndedAt == null, cancellationToken);
        if (session is null) return null;
        var computer = await db.Computers.SingleAsync(x => x.Id == computerId, cancellationToken);
        var nowUtc = DateTime.UtcNow;
        if (session.IsDueToExpire(nowUtc))
        {
            session.MarkTimeExpired(nowUtc);
            await db.SaveChangesAsync(cancellationToken);
        }
        var charge = await db.Charges.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == session.Id, cancellationToken);
        return ToSummary(session, computer.Name, charge);
    }

    public async Task<Computer> AddComputerAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(name));
        if (await db.Computers.AnyAsync(x => x.Name.ToLower() == normalized.ToLower(), cancellationToken)) throw new InvalidOperationException("Ya existe un equipo con ese nombre.");
        var computer = new Computer(normalized);
        db.Computers.Add(computer);
        await db.SaveChangesAsync(cancellationToken);
        return computer;
    }

    public async Task RenameComputerAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontro el equipo.");
        var normalized = name.Trim();
        if (await db.Computers.AnyAsync(x => x.Id != id && x.Name.ToLower() == normalized.ToLower(), cancellationToken)) throw new InvalidOperationException("Ya existe un equipo con ese nombre.");
        computer.Rename(normalized);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SetComputerOutOfServiceAsync(Guid id, CancellationToken cancellationToken = default) => ChangeComputerAsync(id, c => c.SetOutOfService(), cancellationToken);
    public Task RestoreComputerAsync(Guid id, CancellationToken cancellationToken = default) => ChangeComputerAsync(id, c => c.Restore(), cancellationToken);

    private async Task ChangeComputerAsync(Guid id, Action<Computer> change, CancellationToken cancellationToken)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontro el equipo.");
        change(computer);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Tariff> AddTariffAsync(string name, decimal pricePerHour, bool isActive = true, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var tariff = new Tariff(name, pricePerHour, isActive);
        if (await db.Tariffs.AnyAsync(x => x.Name.ToLower() == tariff.Name.ToLower(), cancellationToken)) throw new InvalidOperationException("Ya existe una tarifa con ese nombre.");
        db.Tariffs.Add(tariff);
        await db.SaveChangesAsync(cancellationToken);
        return tariff;
    }

    public async Task UpdateTariffAsync(Guid id, string name, decimal pricePerHour, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var tariff = await db.Tariffs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontro la tarifa.");
        if (await db.Tariffs.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.Trim().ToLower(), cancellationToken)) throw new InvalidOperationException("Ya existe una tarifa con ese nombre.");
        tariff.Update(name, pricePerHour, isActive);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<SessionSummary> StartSessionAsync(Guid computerId, Guid tariffId, CancellationToken cancellationToken = default) =>
        StartSessionAsync(computerId, tariffId, SessionMode.OpenEnded, null, false, cancellationToken);

    public async Task<SessionSummary> StartSessionAsync(Guid computerId, Guid tariffId, SessionMode mode, TimeSpan? contractedDuration, bool payAtStart = false, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == computerId, cancellationToken) ?? throw new KeyNotFoundException("No se encontro el equipo.");
        var tariff = await db.Tariffs.SingleOrDefaultAsync(x => x.Id == tariffId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("La tarifa no existe o esta inactiva.");
        if (await db.Sessions.AnyAsync(x => x.ComputerId == computerId && x.EndedAt == null, cancellationToken)) throw new InvalidOperationException("El equipo ya tiene una sesion activa.");
        if (payAtStart && mode != SessionMode.Prepaid) throw new InvalidOperationException("El tiempo libre no puede cobrarse antes de finalizar.");
        computer.StartSession();
        var session = new Session(computerId, tariff, DateTime.UtcNow, mode, contractedDuration);
        db.Sessions.Add(session);
        if (payAtStart)
        {
            var paidAtUtc = DateTime.UtcNow;
            session.MarkPaid(paidAtUtc);
            db.Charges.Add(new Charge(session.Id, session.FinalAmount!.Value, paidAtUtc));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(session, computer.Name, null);
    }

    public async Task<SessionSummary> FinishSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken) ?? throw new KeyNotFoundException("No se encontro la sesion.");
        var computer = await db.Computers.SingleAsync(x => x.Id == session.ComputerId, cancellationToken);
        if (session.IsActive)
        {
            var nowUtc = DateTime.UtcNow;
            if (session.IsDueToExpire(nowUtc)) session.MarkTimeExpired(nowUtc); else session.Finish(nowUtc);
        }
        else if (session.State != SessionState.TimeExpired)
        {
            throw new InvalidOperationException("La sesion ya fue finalizada.");
        }
        if (session.State == SessionState.TimeExpired)
            session.ReleaseAfterExpiration();
        computer.FinishSession();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(session, computer.Name, await db.Charges.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == session.Id, cancellationToken));
    }

    public Task<SessionSummary> MarkTimeExpiredAsync(Guid sessionId, CancellationToken cancellationToken = default) => ExpireSessionAsync(sessionId, cancellationToken);

    private async Task<SessionSummary> ExpireSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.EndedAt == null, cancellationToken) ?? throw new KeyNotFoundException("No se encontro la sesion activa.");
        session.MarkTimeExpired(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        var computer = await db.Computers.SingleAsync(x => x.Id == session.ComputerId, cancellationToken);
        return ToSummary(session, computer.Name, await db.Charges.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == session.Id, cancellationToken));
    }

    public async Task<SessionSummary> RegisterPaymentAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken) ?? throw new KeyNotFoundException("No se encontro la sesion.");
        if (await db.Charges.AnyAsync(x => x.SessionId == sessionId, cancellationToken)) throw new InvalidOperationException("La sesion ya tiene un cobro registrado.");
        var paidAtUtc = DateTime.UtcNow;
        session.MarkPaid(paidAtUtc);
        db.Charges.Add(new Charge(session.Id, session.FinalAmount!.Value, paidAtUtc));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var computer = await db.Computers.AsNoTracking().SingleAsync(x => x.Id == session.ComputerId, cancellationToken);
        return ToSummary(session, computer.Name, await db.Charges.AsNoTracking().SingleAsync(x => x.SessionId == session.Id, cancellationToken));
    }

    public async Task<decimal> GetRevenueAsync(DateTime localFrom, DateTime localTo, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        if (localTo <= localFrom) throw new ArgumentException("El final del periodo debe ser posterior al inicio.", nameof(localTo));
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localFrom, DateTimeKind.Unspecified), timeZone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTo, DateTimeKind.Unspecified), timeZone);
        await using var db = await CreateContextAsync(cancellationToken);
        return await db.Charges.Where(x => x.ChargedAtUtc >= fromUtc && x.ChargedAtUtc < toUtc).SumAsync(x => x.Amount, cancellationToken);
    }

    public async Task<DashboardSummary> GetDashboardAsync(DateTime localDate, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var startLocal = localDate.Date;
        await using var db = await CreateContextAsync(cancellationToken);
        var total = await db.Computers.CountAsync(cancellationToken);
        var activeRows = await db.Sessions.AsNoTracking().Where(x => x.EndedAt == null).Select(x => new { x.StartedAt, x.HourlyRate, x.Mode, x.ContractedDurationSeconds }).ToListAsync(cancellationToken);
        var activeEstimated = activeRows.Sum(x => x.Mode == SessionMode.Prepaid ? Session.CalculateAmount(TimeSpan.FromSeconds(x.ContractedDurationSeconds!.Value), x.HourlyRate) : Session.CalculateAmount(DateTime.UtcNow - x.StartedAt, x.HourlyRate));
        var revenue = await GetRevenueAsync(startLocal, startLocal.AddDays(1), timeZone, cancellationToken);
        return new DashboardSummary(total, activeRows.Count, revenue, activeEstimated);
    }

    private static SessionSummary ToSummary(Session session, string computerName, Charge? charge)
    {
        var nowUtc = DateTime.UtcNow;
        var end = session.EndedAt;
        var duration = end.HasValue ? end.Value - session.StartedAt : session.GetElapsed(nowUtc);
        if (session.Mode == SessionMode.Prepaid) duration = TimeSpan.FromSeconds(Math.Min(duration.TotalSeconds, session.ContractedDurationSeconds!.Value));
        var isEstimated = session.IsActive && session.Mode == SessionMode.OpenEnded;
        var amount = isEstimated ? session.CalculateAmount(nowUtc) : charge?.Amount ?? session.FinalAmount ?? 0m;
        return new SessionSummary(session.Id, session.ComputerId, computerName, session.TariffName, session.StartedAt, end, session.HourlyRate, amount, duration, session.Mode, session.State, session.PaymentStatus, isEstimated, charge?.ChargedAtUtc);
    }
}
