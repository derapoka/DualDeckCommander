namespace DualDeckCommander;

/// <summary>Képpontértékek méretezése a rendszer DPI-beállításához (pl. 125%, 150%).</summary>
internal static class UiScale
{
    public static readonly float Factor = GetFactor();

    private static float GetFactor()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch
        {
            return 1f;
        }
    }

    public static int S(int value) => (int)Math.Round(value * Factor);

    public static Size S(int width, int height) => new(S(width), S(height));
}
