using Microsoft.EntityFrameworkCore;
using Nexora.Core.Models;

namespace Nexora.Infrastructure.Persistence;

public sealed class NexoraDbContext(DbContextOptions<NexoraDbContext> options) : DbContext(options)
{
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Charge> Charges => Set<Charge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Computer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).UseCollation("NOCASE").IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<Tariff>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).UseCollation("NOCASE").IsRequired();
            entity.Property(x => x.PricePerHour).HasPrecision(18, 2).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Mode).HasConversion<string>().IsRequired();
            entity.Property(x => x.State).HasConversion<string>().IsRequired();
            entity.Property(x => x.PaymentStatus).HasConversion<string>().IsRequired();
            entity.Property(x => x.TariffName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.HourlyRate).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.ContractedDurationSeconds);
            entity.Property(x => x.PaidAtUtc);
            entity.Property(x => x.FinalAmount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.ComputerId, x.EndedAt })
                .HasDatabaseName("IX_Sessions_Computer_Active")
                .IsUnique()
                .HasFilter("EndedAt IS NULL");
            entity.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Charge>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.ChargedAtUtc).IsRequired();
            entity.HasIndex(x => x.SessionId).IsUnique();
            entity.HasIndex(x => x.ChargedAtUtc);
            entity.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
