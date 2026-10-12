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
    TimeSpan Duration);
