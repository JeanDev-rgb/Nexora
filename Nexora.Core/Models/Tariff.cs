namespace Nexora.Core.Models;

public class Tariff
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public decimal PricePerHour { get; private set; }
    public bool IsActive { get; private set; }

    private Tariff() { }

    public Tariff(string name, decimal pricePerHour, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre de la tarifa es obligatorio.", nameof(name));
        if (pricePerHour <= 0 || decimal.Round(pricePerHour, 2) != pricePerHour)
            throw new ArgumentOutOfRangeException(nameof(pricePerHour), "El precio debe ser positivo y tener como máximo dos decimales.");

        Name = name.Trim();
        PricePerHour = pricePerHour;
        IsActive = isActive;
    }

    public void Update(string name, decimal pricePerHour, bool isActive)
    {
        var updated = new Tariff(name, pricePerHour, isActive);
        Name = updated.Name;
        PricePerHour = updated.PricePerHour;
        IsActive = updated.IsActive;
    }
}
