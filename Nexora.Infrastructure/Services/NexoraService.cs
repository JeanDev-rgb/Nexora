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
        var tableCount = Convert.ToInt32(await tablesCommand.ExecuteScalarAsync(cancellationToken));
        if (tableCount != 3)
            return;

        await using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        var hasHistory = Convert.ToInt32(await historyCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasHistory)
            return;

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
            .Join(db.Computers, session => session.ComputerId, computer => computer.Id,
                (session, computer) => new { session, computer.Name })
            .GroupJoin(db.Charges, row => row.session.Id, charge => charge.SessionId,
                (row, charges) => new { row.session, row.Name, Charge = charges.SingleOrDefault() })
            .OrderByDescending(x => x.session.EndedAt).Take(count).ToListAsync(cancellationToken);
        return rows.Select(x => ToSummary(x.session, x.Name, x.Charge)).ToList();
    }

    public async Task<SessionSummary?> GetActiveSessionAsync(Guid computerId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var row = await db.Sessions.AsNoTracking().Where(x => x.ComputerId == computerId && x.EndedAt == null)
            .Join(db.Computers, session => session.ComputerId, computer => computer.Id,
                (session, computer) => new { session, computer.Name }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToSummary(row.session, row.Name, null);
    }

    public async Task<Computer> AddComputerAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(name));
        if (await db.Computers.AnyAsync(x => x.Name.ToLower() == normalized.ToLower(), cancellationToken))
            throw new InvalidOperationException("Ya existe un equipo con ese nombre.");
        var computer = new Computer(normalized);
        db.Computers.Add(computer);
        await db.SaveChangesAsync(cancellationToken);
        return computer;
    }

    public async Task RenameComputerAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontró el equipo.");
        var normalized = name.Trim();
        if (await db.Computers.AnyAsync(x => x.Id != id && x.Name.ToLower() == normalized.ToLower(), cancellationToken))
            throw new InvalidOperationException("Ya existe un equipo con ese nombre.");
        computer.Rename(normalized);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SetComputerOutOfServiceAsync(Guid id, CancellationToken cancellationToken = default) => ChangeComputerAsync(id, c => c.SetOutOfService(), cancellationToken);
    public Task RestoreComputerAsync(Guid id, CancellationToken cancellationToken = default) => ChangeComputerAsync(id, c => c.Restore(), cancellationToken);

    private async Task ChangeComputerAsync(Guid id, Action<Computer> change, CancellationToken cancellationToken)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontró el equipo.");
        change(computer);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Tariff> AddTariffAsync(string name, decimal pricePerHour, bool isActive = true, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var tariff = new Tariff(name, pricePerHour, isActive);
        if (await db.Tariffs.AnyAsync(x => x.Name.ToLower() == tariff.Name.ToLower(), cancellationToken))
            throw new InvalidOperationException("Ya existe una tarifa con ese nombre.");
        db.Tariffs.Add(tariff);
        await db.SaveChangesAsync(cancellationToken);
        return tariff;
    }

    public async Task UpdateTariffAsync(Guid id, string name, decimal pricePerHour, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        var tariff = await db.Tariffs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("No se encontró la tarifa.");
        if (await db.Tariffs.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.Trim().ToLower(), cancellationToken))
            throw new InvalidOperationException("Ya existe una tarifa con ese nombre.");
        tariff.Update(name, pricePerHour, isActive);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SessionSummary> StartSessionAsync(Guid computerId, Guid tariffId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var computer = await db.Computers.SingleOrDefaultAsync(x => x.Id == computerId, cancellationToken) ?? throw new KeyNotFoundException("No se encontró el equipo.");
        var tariff = await db.Tariffs.SingleOrDefaultAsync(x => x.Id == tariffId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("La tarifa no existe o está inactiva.");
        if (await db.Sessions.AnyAsync(x => x.ComputerId == computerId && x.EndedAt == null, cancellationToken))
            throw new InvalidOperationException("El equipo ya tiene una sesión activa.");
        computer.StartSession();
        var session = new Session(computerId, tariff, DateTime.UtcNow);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(session, computer.Name, null);
    }

    public async Task<SessionSummary> FinishSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken) ?? throw new KeyNotFoundException("No se encontró la sesión.");
        var computer = await db.Computers.SingleAsync(x => x.Id == session.ComputerId, cancellationToken);
        var chargedAtUtc = DateTime.UtcNow;
        session.Finish(chargedAtUtc);
        computer.FinishSession();
        db.Charges.Add(new Charge(session.Id, session.FinalAmount ?? throw new InvalidOperationException("La sesión no tiene importe final."), chargedAtUtc));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(session, computer.Name, await db.Charges.AsNoTracking().SingleAsync(x => x.SessionId == session.Id, cancellationToken));
    }

    public async Task<DashboardSummary> GetDashboardAsync(DateTime localDate, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var startLocal = localDate.Date;
        await using var db = await CreateContextAsync(cancellationToken);
        var total = await db.Computers.CountAsync(cancellationToken);
        var activeRows = await db.Sessions.AsNoTracking()
            .Where(x => x.EndedAt == null)
            .Select(x => new { x.StartedAt, x.HourlyRate })
            .ToListAsync(cancellationToken);
        var active = activeRows.Count;
        var activeEstimated = activeRows.Sum(x => Session.CalculateAmount(DateTime.UtcNow - x.StartedAt, x.HourlyRate));
        var revenue = await GetRevenueAsync(startLocal, startLocal.AddDays(1), timeZone, cancellationToken);
        return new DashboardSummary(total, active, revenue, activeEstimated);
    }

    public async Task<decimal> GetRevenueAsync(DateTime localFrom, DateTime localTo, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        if (localTo <= localFrom)
            throw new ArgumentException("El final del período debe ser posterior al inicio.", nameof(localTo));

        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localFrom, DateTimeKind.Unspecified), timeZone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTo, DateTimeKind.Unspecified), timeZone);
        await using var db = await CreateContextAsync(cancellationToken);
        return await db.Charges.Where(x => x.ChargedAtUtc >= fromUtc && x.ChargedAtUtc < toUtc)
            .SumAsync(x => x.Amount, cancellationToken);
    }

    private static SessionSummary ToSummary(Session session, string computerName, Charge? charge)
    {
        var end = session.EndedAt;
        var duration = end.HasValue ? end.Value - session.StartedAt : DateTime.UtcNow - session.StartedAt;
        var isEstimated = session.IsActive;
        var amount = isEstimated ? session.CalculateAmount(DateTime.UtcNow) : charge?.Amount ?? session.FinalAmount ?? 0m;
        return new SessionSummary(session.Id, session.ComputerId, computerName, session.TariffName, session.StartedAt, end, session.HourlyRate, amount, duration, isEstimated, charge?.ChargedAtUtc);
    }
}
