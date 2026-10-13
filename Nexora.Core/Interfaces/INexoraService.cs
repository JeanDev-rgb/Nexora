using Nexora.Core.Enums;
using Nexora.Core.Models;

namespace Nexora.Core.Interfaces;

public interface INexoraService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Computer>> GetComputersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tariff>> GetTariffsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<SessionSummary?> GetActiveSessionAsync(Guid computerId, CancellationToken cancellationToken = default);
    Task<Computer> AddComputerAsync(string name, CancellationToken cancellationToken = default);
    Task RenameComputerAsync(Guid id, string name, CancellationToken cancellationToken = default);
    Task SetComputerOutOfServiceAsync(Guid id, CancellationToken cancellationToken = default);
    Task RestoreComputerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Tariff> AddTariffAsync(string name, decimal pricePerHour, bool isActive = true, CancellationToken cancellationToken = default);
    Task UpdateTariffAsync(Guid id, string name, decimal pricePerHour, bool isActive, CancellationToken cancellationToken = default);
    Task<SessionSummary> StartSessionAsync(Guid computerId, Guid tariffId, CancellationToken cancellationToken = default);
    Task<SessionSummary> StartSessionAsync(Guid computerId, Guid tariffId, SessionMode mode, TimeSpan? contractedDuration, bool payAtStart = false, CancellationToken cancellationToken = default);
    Task<SessionSummary> FinishSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionSummary> MarkTimeExpiredAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionSummary> RegisterPaymentAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<decimal> GetRevenueAsync(DateTime localFrom, DateTime localTo, TimeZoneInfo timeZone, CancellationToken cancellationToken = default);
    Task<DashboardSummary> GetDashboardAsync(DateTime localDate, TimeZoneInfo timeZone, CancellationToken cancellationToken = default);
}

public sealed record DashboardSummary(int TotalComputers, int ActiveSessions, decimal TodayRevenue, decimal ActiveEstimatedAmount);
