using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.UnitTests;

public sealed class WorkerCadenceTests
{
    [Fact]
    public void QueueCanBeCheckedEveryMinuteWithoutForcingAnExternalCall()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), WorkerCadence.QueueCheck(null));
        Assert.Equal(TimeSpan.FromMinutes(1), WorkerCadence.QueueCheck(0));
        var start = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        Assert.True(WorkerCadence.ExternalDue(start, null, TimeSpan.FromMinutes(10)));
        Assert.False(WorkerCadence.ExternalDue(start.AddMinutes(9), start, TimeSpan.FromMinutes(10)));
        Assert.True(WorkerCadence.ExternalDue(start.AddMinutes(10), start, TimeSpan.FromMinutes(10)));
    }
}
