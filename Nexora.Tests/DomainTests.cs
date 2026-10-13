using Nexora.Core.Enums;
using Nexora.Core.Models;
using Xunit;

namespace Nexora.Tests;

public class DomainTests
{
    [Fact]
    public void Computer_rejects_invalid_transitions()
    {
        var computer = new Computer("PC-01");
        computer.StartSession();
        Assert.Equal(ComputerStatus.InUse, computer.Status);
        Assert.Throws<InvalidOperationException>(() => computer.StartSession());
        Assert.Throws<InvalidOperationException>(() => computer.SetOutOfService());
        computer.FinishSession();
        computer.SetOutOfService();
        Assert.Throws<InvalidOperationException>(() => computer.StartSession());
    }

    [Fact]
    public void Session_uses_snapshot_rate_and_rounds_up_to_started_minute()
    {
        var tariff = new Tariff("Gaming", 30m);
        var start = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var session = new Session(Guid.NewGuid(), tariff, start);
        session.Finish(start.AddMinutes(61).AddSeconds(1));
        Assert.Equal(31m, session.FinalAmount);
        tariff.Update("Gaming", 60m, true);
        Assert.Equal(30m, session.HourlyRate);
        Assert.Throws<InvalidOperationException>(() => session.Finish(start.AddHours(2)));
    }

    [Fact]
    public void Tariff_rejects_zero_negative_and_more_than_two_decimals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tariff("Zero", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tariff("Negative", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tariff("Invalid", 1.999m));
    }

    [Fact]
    public void Session_rejects_end_before_start()
    {
        var start = DateTime.UtcNow;
        var session = new Session(Guid.NewGuid(), new Tariff("Basic", 10), start);
        Assert.Throws<ArgumentException>(() => session.Finish(start.AddSeconds(-1)));
    }

    [Fact]
    public void Active_amount_is_deterministic_from_duration_and_snapshot_rate()
    {
        Assert.Equal(0.50m, Session.CalculateAmount(TimeSpan.FromSeconds(1), 30m));
        Assert.Equal(0.50m, Session.CalculateAmount(TimeSpan.FromMinutes(1), 30m));
        Assert.Equal(1.5m, Session.CalculateAmount(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1), 30m));
    }

    [Fact]
    public void Prepaid_session_keeps_fixed_price_and_never_has_negative_remaining_time()
    {
        var start = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var session = new Session(Guid.NewGuid(), new Tariff("Paquete", 60m), start,
            SessionMode.Prepaid, TimeSpan.FromMinutes(30));

        Assert.Equal(30m, session.FinalAmount);
        Assert.Equal(TimeSpan.FromMinutes(30), session.GetRemaining(start));
        Assert.Equal(TimeSpan.Zero, session.GetRemaining(start.AddMinutes(31)));
        Assert.Equal(30m, session.CalculateAmount(start.AddHours(2)));
    }

    [Fact]
    public void Expired_prepaid_session_requires_explicit_release()
    {
        var start = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var session = new Session(Guid.NewGuid(), new Tariff("Paquete", 60m), start,
            SessionMode.Prepaid, TimeSpan.FromMinutes(15));

        session.MarkTimeExpired(start.AddMinutes(15));

        Assert.Equal(SessionState.TimeExpired, session.State);
        Assert.Null(session.EndedAt);
        Assert.Throws<InvalidOperationException>(() => session.MarkTimeExpired(start.AddMinutes(16)));
        session.ReleaseAfterExpiration();
        Assert.Equal(start.AddMinutes(15), session.EndedAt);
    }
}
