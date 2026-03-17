namespace Warband.Core;

public static class TimeScale
{
    public static readonly float[] Steps = [0f, 1f, 2f, 4f, 8f];

    public static int ClampIndex(int index) => Math.Clamp(index, 0, Steps.Length - 1);

    public static float FromIndex(int index) => Steps[ClampIndex(index)];
}

