namespace Warband.Core;

public static class TimeScale
{
	// Keep aligned with README Phase 0 Step 0.1 defaults (pause / 1x / 2x / 5x).
	public static readonly float[] Steps = [0f, 1f, 2f, 5f];

	public static int ClampIndex(int index) => Math.Clamp(index, 0, Steps.Length - 1);

	public static float FromIndex(int index) => Steps[ClampIndex(index)];
}
