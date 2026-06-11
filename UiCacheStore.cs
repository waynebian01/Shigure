using System.Text.Json;

namespace Shigure;

internal static class UiCacheStore
{
    private const string CacheFolderName = "cache";
    private const string CacheFileName = "window-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CacheDirectory => Path.Combine(Environment.CurrentDirectory, CacheFolderName);
    private static string CacheFilePath => Path.Combine(CacheDirectory, CacheFileName);

    public static UiCacheState Load()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return new UiCacheState();
            }

            var json = File.ReadAllText(CacheFilePath);
            return JsonSerializer.Deserialize<UiCacheState>(json) ?? new UiCacheState();
        }
        catch
        {
            return new UiCacheState();
        }
    }

    public static void Save(UiCacheState state)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(CacheFilePath, json);
        }
        catch
        {
            // 忽略缓存写入异常，避免影响主流程。
        }
    }

    public static bool IsBoundsVisible(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
    }
}

internal sealed class UiCacheState
{
    public WindowLocation? MainWindowLocation { get; set; }
    public WindowBounds? SettingsWindowBounds { get; set; }
    public string? ToggleKey { get; set; }
}

internal sealed class WindowLocation
{
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
