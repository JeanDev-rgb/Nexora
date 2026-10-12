namespace Nexora.Core.Models;

public class Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ComputerId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; private set; }
    public string TariffName { get; init; } = string.Empty;
    public decimal HourlyRate { get; init; }
    public decimal? FinalAmount { get; private set; }
    public bool IsActive => EndedAt is null;

    private Session() { }

    public Session(Guid computerId, Tariff tariff, DateTime startedAtUtc)
    {
        if (computerId == Guid.Empty)
            throw new ArgumentException("El equipo es obligatorio.", nameof(computerId));
        if (tariff is null)
            throw new ArgumentNullException(nameof(tariff));
        if (startedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha debe estar expresada en UTC.", nameof(startedAtUtc));

        ComputerId = computerId;
        TariffName = tariff.Name;
        HourlyRate = tariff.PricePerHour;
        StartedAt = startedAtUtc;
    }

    public TimeSpan GetElapsed(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha debe estar expresada en UTC.", nameof(nowUtc));
        var end = EndedAt ?? nowUtc;
        if (end < StartedAt)
            throw new InvalidOperationException("La fecha de finalización no puede ser anterior al inicio.");
        return end - StartedAt;
    }

    // Cobro por minuto iniciado, con importe monetario redondeado a 2 decimales.
    public decimal CalculateAmount(DateTime nowUtc)
    {
        var elapsed = GetElapsed(nowUtc);
        var billableMinutes = (long)Math.Ceiling(elapsed.TotalMinutes);
        return Math.Round(billableMinutes * HourlyRate / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public void Finish(DateTime endedAtUtc)
    {
        if (EndedAt is not null)
            throw new InvalidOperationException("La sesión ya ha finalizado.");
        if (endedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha debe estar expresada en UTC.", nameof(endedAtUtc));
        if (endedAtUtc < StartedAt)
            throw new ArgumentException("La fecha de finalización no puede ser anterior al inicio.", nameof(endedAtUtc));

        EndedAt = endedAtUtc;
        FinalAmount = CalculateAmount(endedAtUtc);
    }
}
