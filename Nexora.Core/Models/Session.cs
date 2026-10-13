using Nexora.Core.Enums;

namespace Nexora.Core.Models;

public class Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ComputerId { get; init; }
    public SessionMode Mode { get; private set; }
    public SessionState State { get; private set; } = SessionState.Active;
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public string TariffName { get; init; } = string.Empty;
    public decimal HourlyRate { get; init; }
    public long? ContractedDurationSeconds { get; private set; }
    public decimal? FinalAmount { get; private set; }
    public bool IsActive => State == SessionState.Active;

    private Session() { }

    public Session(Guid computerId, Tariff tariff, DateTime startedAtUtc,
        SessionMode mode = SessionMode.OpenEnded, TimeSpan? contractedDuration = null)
    {
        if (computerId == Guid.Empty) throw new ArgumentException("El equipo es obligatorio.", nameof(computerId));
        if (tariff is null) throw new ArgumentNullException(nameof(tariff));
        if (startedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La fecha debe estar expresada en UTC.", nameof(startedAtUtc));
        if (mode == SessionMode.Prepaid && (!contractedDuration.HasValue || contractedDuration.Value <= TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(contractedDuration), "El tiempo contratado debe ser mayor que cero.");
        if (mode == SessionMode.OpenEnded && contractedDuration.HasValue)
            throw new ArgumentException("El tiempo libre no admite una duración contratada.", nameof(contractedDuration));

        ComputerId = computerId;
        Mode = mode;
        TariffName = tariff.Name;
        HourlyRate = tariff.PricePerHour;
        StartedAt = startedAtUtc;
        ContractedDurationSeconds = contractedDuration?.Ticks / TimeSpan.TicksPerSecond;
        if (mode == SessionMode.Prepaid) FinalAmount = CalculateAmount(contractedDuration!.Value, HourlyRate);
    }

    public TimeSpan GetElapsed(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        var end = EndedAt ?? nowUtc;
        if (end < StartedAt) throw new InvalidOperationException("La fecha de finalización no puede ser anterior al inicio.");
        return end - StartedAt;
    }

    public TimeSpan GetRemaining(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        if (Mode != SessionMode.Prepaid) throw new InvalidOperationException("Las sesiones de tiempo libre no tienen cuenta atrás.");
        var remaining = TimeSpan.FromSeconds(ContractedDurationSeconds!.Value) - GetElapsed(nowUtc);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsDueToExpire(DateTime nowUtc) => Mode == SessionMode.Prepaid && IsActive && GetRemaining(nowUtc) == TimeSpan.Zero;

    public decimal CalculateAmount(DateTime nowUtc) => Mode == SessionMode.Prepaid ? FinalAmount!.Value : CalculateAmount(GetElapsed(nowUtc), HourlyRate);

    public static decimal CalculateAmount(TimeSpan duration, decimal hourlyRate)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (hourlyRate <= 0) throw new ArgumentOutOfRangeException(nameof(hourlyRate));
        var billableMinutes = (long)Math.Ceiling(duration.TotalMinutes);
        return Math.Round(billableMinutes * hourlyRate / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public void Finish(DateTime endedAtUtc)
    {
        ValidateUtc(endedAtUtc);
        if (!IsActive) throw new InvalidOperationException("La sesión ya no está activa.");
        if (endedAtUtc < StartedAt) throw new ArgumentException("La fecha de finalización no puede ser anterior al inicio.", nameof(endedAtUtc));
        EndedAt = Mode == SessionMode.Prepaid
            ? StartedAt + TimeSpan.FromSeconds(ContractedDurationSeconds!.Value) < endedAtUtc
                ? StartedAt + TimeSpan.FromSeconds(ContractedDurationSeconds.Value) : endedAtUtc
            : endedAtUtc;
        if (Mode == SessionMode.OpenEnded) FinalAmount = CalculateAmount(EndedAt.Value);
        State = SessionState.Completed;
    }

    public void MarkTimeExpired(DateTime nowUtc)
    {
        if (!IsDueToExpire(nowUtc)) throw new InvalidOperationException("La sesión no está lista para marcarse como expirada.");
        State = SessionState.TimeExpired;
    }

    public void ReleaseAfterExpiration()
    {
        if (Mode != SessionMode.Prepaid || State != SessionState.TimeExpired)
            throw new InvalidOperationException("La sesión no está expirada.");

        EndedAt ??= StartedAt + TimeSpan.FromSeconds(ContractedDurationSeconds!.Value);
    }

    public void MarkPaid(DateTime paidAtUtc)
    {
        ValidateUtc(paidAtUtc);
        if (PaymentStatus == PaymentStatus.Paid) throw new InvalidOperationException("La sesión ya tiene un cobro registrado.");
        if (FinalAmount is null || (State == SessionState.Active && Mode != SessionMode.Prepaid)) throw new InvalidOperationException("La sesión debe estar finalizada antes de cobrarla.");
        PaymentStatus = PaymentStatus.Paid;
        PaidAtUtc = paidAtUtc;
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("La fecha debe estar expresada en UTC.", nameof(value));
    }
}
