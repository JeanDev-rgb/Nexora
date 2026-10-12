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
}
