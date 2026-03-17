using NUnit.Framework;
using Warband.Core;

namespace Warband.Core.Tests;

public sealed class FixedTickClockTests
{
	[Test]
	public void PauseProducesNoTicks()
	{
		var clock = new FixedTickClock(ticksPerSecond: 60);

		var ticks = clock.Advance(realDeltaSeconds: 1.0, timeScale: 0.0, maxTicksThisAdvance: 10_000);

		Assert.That(ticks, Is.EqualTo(0));
		Assert.That(clock.TotalTicks, Is.EqualTo(0));
	}

	[Test]
	public void OneSecondAt60HzProduces60Ticks()
	{
		var clock = new FixedTickClock(ticksPerSecond: 60);

		var ticks = clock.Advance(realDeltaSeconds: 1.0, timeScale: 1.0, maxTicksThisAdvance: 10_000);

		Assert.That(ticks, Is.EqualTo(60));
		Assert.That(clock.TotalTicks, Is.EqualTo(60));
	}

	[Test]
	public void FastForwardScalesTickProduction()
	{
		var clock = new FixedTickClock(ticksPerSecond: 60);

		var ticks = clock.Advance(realDeltaSeconds: 0.5, timeScale: 4.0, maxTicksThisAdvance: 10_000);

		// 0.5s * 4x = 2.0s of sim time => 120 ticks at 60Hz
		Assert.That(ticks, Is.EqualTo(120));
		Assert.That(clock.TotalTicks, Is.EqualTo(120));
	}

	[Test]
	public void CatchUpClampLimitsTicksPerAdvance()
	{
		var clock = new FixedTickClock(ticksPerSecond: 60);

		var ticks = clock.Advance(realDeltaSeconds: 10.0, timeScale: 1.0, maxTicksThisAdvance: 5);

		Assert.That(ticks, Is.EqualTo(5));
		Assert.That(clock.TotalTicks, Is.EqualTo(5));
	}

	[Test]
	public void IdenticalInputsYieldIdenticalTickCounts()
	{
		var a = new FixedTickClock(ticksPerSecond: 30);
		var b = new FixedTickClock(ticksPerSecond: 30);

		var deltas = new[] { 0.010, 0.020, 0.033, 0.016, 0.050, 0.100, 0.001 };

		var producedA = new List<int>();
		var producedB = new List<int>();

		foreach (var d in deltas)
		{
			producedA.Add(a.Advance(realDeltaSeconds: d, timeScale: 2.0, maxTicksThisAdvance: 1000));
			producedB.Add(b.Advance(realDeltaSeconds: d, timeScale: 2.0, maxTicksThisAdvance: 1000));
		}

		Assert.That(producedA, Is.EqualTo(producedB));
		Assert.That(a.TotalTicks, Is.EqualTo(b.TotalTicks));
		Assert.That(a.SimTimeSeconds, Is.EqualTo(b.SimTimeSeconds));
	}
}
