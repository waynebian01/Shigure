using System.Text.Json;

namespace Shigure;

internal static class UiCacheStore
{
    private const string CacheFolderName = "cache";
    private const string CacheFileName = "window-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CacheDirectory => Path.Combine(AppPaths.UserDataDirectory, CacheFolderName);
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

    public static Dictionary<string, int>? LoadColumnWidths(string cacheKey)
    {
        var state = Load();
        if (state.ColumnWidths?.TryGetValue(cacheKey, out var widths) == true)
        {
            return new Dictionary<string, int>(widths, StringComparer.Ordinal);
        }

        // 兼容旧版本只保存模块逻辑编辑列宽的字段。
        if (string.Equals(cacheKey, "module-rules", StringComparison.Ordinal)
            && state.ModuleRulesGridColumns is { Count: > 0 } legacyWidths)
        {
            return new Dictionary<string, int>(legacyWidths, StringComparer.Ordinal);
        }

        return null;
    }

    public static void SaveColumnWidth(string cacheKey, string columnKey, int width)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(columnKey) || width <= 0)
        {
            return;
        }

        var state = Load();
        state.ColumnWidths ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        if (!state.ColumnWidths.TryGetValue(cacheKey, out var widths))
        {
            widths = LoadColumnWidths(cacheKey) ?? new Dictionary<string, int>(StringComparer.Ordinal);
            state.ColumnWidths[cacheKey] = widths;
        }

        widths[columnKey] = width;
        if (string.Equals(cacheKey, "module-rules", StringComparison.Ordinal))
        {
            state.ModuleRulesGridColumns ??= new Dictionary<string, int>(StringComparer.Ordinal);
            state.ModuleRulesGridColumns[columnKey] = width;
        }

        Save(state);
    }
}

internal sealed class UiCacheState
{
    public WindowLocation? MainWindowLocation { get; set; }
    public WindowBounds? MainWindowBounds { get; set; }
    public WindowBounds? HorizontalMainWindowBounds { get; set; }
    public WindowBounds? VerticalMainWindowBounds { get; set; }
    public WindowBounds? SettingsWindowBounds { get; set; }
    public string? SelectedSettingsPage { get; set; }
    public string? MainWindowLayout { get; set; }
    public string? CloseButtonBehavior { get; set; }
    public string? ToggleKey { get; set; }
    public string? SelectedModuleId { get; set; }
    public Dictionary<string, int>? ModuleRulesGridColumns { get; set; }
    public Dictionary<string, Dictionary<string, int>>? ColumnWidths { get; set; }
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
