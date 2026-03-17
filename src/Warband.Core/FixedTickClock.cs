namespace Warband.Core;

public sealed class FixedTickClock
{
    private double _accumulatorSeconds;

    public FixedTickClock(int ticksPerSecond)
    {
        if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        TicksPerSecond = ticksPerSecond;
        TickDeltaSeconds = 1.0 / ticksPerSecond;
    }

    public int TicksPerSecond { get; }
    public double TickDeltaSeconds { get; }

    public long TotalTicks { get; private set; }
    public double SimTimeSeconds => TotalTicks * TickDeltaSeconds;

    public void Reset()
    {
        _accumulatorSeconds = 0;
        TotalTicks = 0;
    }

    public int Advance(
        double realDeltaSeconds,
        double timeScale,
        int maxTicksThisAdvance)
    {
        if (realDeltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds));
        if (timeScale < 0) throw new ArgumentOutOfRangeException(nameof(timeScale));
        if (maxTicksThisAdvance < 0) throw new ArgumentOutOfRangeException(nameof(maxTicksThisAdvance));

        if (timeScale == 0 || realDeltaSeconds == 0 || maxTicksThisAdvance == 0) return 0;

        _accumulatorSeconds += realDeltaSeconds * timeScale;

        var produced = (int)Math.Floor(_accumulatorSeconds / TickDeltaSeconds);
        if (produced <= 0) return 0;

        if (produced > maxTicksThisAdvance) produced = maxTicksThisAdvance;

        _accumulatorSeconds -= produced * TickDeltaSeconds;
        TotalTicks += produced;
        return produced;
    }
}

