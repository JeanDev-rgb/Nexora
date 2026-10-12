namespace Nexora.Core.Models;

/// <summary>
/// Registro inmutable de un cobro confirmado para una sesión.
/// Una sesión solo puede tener un cobro.
/// </summary>
public sealed class Charge
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    public decimal Amount { get; init; }
    public DateTime ChargedAtUtc { get; init; }

    private Charge() { }

    public Charge(Guid sessionId, decimal amount, DateTime chargedAtUtc)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("La sesión es obligatoria.", nameof(sessionId));
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (chargedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha de cobro debe estar expresada en UTC.", nameof(chargedAtUtc));

        SessionId = sessionId;
        Amount = amount;
        ChargedAtUtc = chargedAtUtc;
    }
}
