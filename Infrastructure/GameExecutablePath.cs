namespace Shigure;

internal static class GameExecutablePath
{
    public static bool TryValidate(
        string? path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "尚未选择游戏程序。";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "游戏程序路径无效。";
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "请选择游戏的 .exe 可执行文件。";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = $"找不到游戏程序：{fullPath}";
            return false;
        }

        return true;
    }
}
