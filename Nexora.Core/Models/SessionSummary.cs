using Nexora.Core.Enums;

namespace Nexora.Core.Models;

public sealed record SessionSummary(
    Guid Id,
    Guid ComputerId,
    string ComputerName,
    string TariffName,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    decimal HourlyRate,
    decimal Amount,
    TimeSpan Duration,
    SessionMode Mode,
    SessionState State,
    PaymentStatus PaymentStatus,
    bool IsEstimated,
    DateTime? ChargedAtUtc)
{
    public bool IsPending => PaymentStatus == PaymentStatus.Pending;
    public TimeSpan? ContractedDuration { get; init; }
}
