using Nexora.Core.Enums;

namespace Nexora.Core.Models;

public class Computer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public ComputerStatus Status { get; private set; } = ComputerStatus.Available;

    private Computer() { }

    public Computer(string name)
    {
        Rename(name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(name));

        Name = name.Trim();
    }

    public void StartSession()
    {
        if (Status != ComputerStatus.Available)
            throw new InvalidOperationException("El equipo no está disponible para iniciar una sesión.");

        Status = ComputerStatus.InUse;
    }

    public void FinishSession()
    {
        if (Status != ComputerStatus.InUse)
            throw new InvalidOperationException("El equipo no tiene una sesión activa.");

        Status = ComputerStatus.Available;
    }

    public void SetOutOfService()
    {
        if (Status == ComputerStatus.InUse)
            throw new InvalidOperationException("No se puede retirar un equipo con una sesión activa.");

        Status = ComputerStatus.OutOfService;
    }

    public void Restore()
    {
        if (Status != ComputerStatus.OutOfService)
            throw new InvalidOperationException("El equipo ya está habilitado.");

        Status = ComputerStatus.Available;
    }
}
