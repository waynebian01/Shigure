namespace Shigure;

/// <summary>
/// 从目标窗口进程定位 Fuyutsui 插件目录与相关文件。
/// </summary>
internal static class WowAddonLocator
{
    private const string InterfaceDirectoryName = "Interface";
    private const string AddOnsDirectoryName = "AddOns";

    public static string? FindClassDirectory(WowProcessLocator processLocator, string addonName = "Fuyutsui")
    {
        var addonRoot = FindAddonRoot(processLocator, addonName);
        if (addonRoot is null)
        {
            return null;
        }

        var classDirectory = Path.Combine(addonRoot, "class");
        return Directory.Exists(classDirectory) ? classDirectory : null;
    }

    /// <summary>定位 Fuyutsui 插件根目录（含 class/、core/）。</summary>
    public static string? FindAddonRoot(WowProcessLocator processLocator, string addonName = "Fuyutsui")
    {
        var addOnsDirectory = FindAddOnsDirectory(processLocator);
        if (addOnsDirectory is null)
        {
            return null;
        }

        var addonRoot = Path.Combine(addOnsDirectory, addonName);
        return Directory.Exists(addonRoot) ? addonRoot : null;
    }

    /// <summary>
    /// 从所选游戏程序定位同级 Interface\AddOns。目录尚未创建时仍返回预期路径，
    /// 不向父目录回溯，以免将相邻版本部署到同一个安装根目录。
    /// </summary>
    public static string? FindAddOnsDirectory(WowProcessLocator processLocator)
    {
        var exePath = processLocator.FindFrontmostProcessPath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        return FindAddOnsDirectoryFromProcessPath(exePath);
    }

    public static string? FindAddOnsDirectoryFromProcessPath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        var executableDirectory = Path.GetDirectoryName(exePath);
        return string.IsNullOrWhiteSpace(executableDirectory)
            ? null
            : Path.Combine(executableDirectory, InterfaceDirectoryName, AddOnsDirectoryName);
    }

    public static string? FindClassMacrosPath(WowProcessLocator processLocator, string addonName = "Fuyutsui")
    {
        var addonRoot = FindAddonRoot(processLocator, addonName);
        if (addonRoot is null)
        {
            return null;
        }

        var macrosPath = Path.Combine(addonRoot, "core", "classmacros.lua");
        return File.Exists(macrosPath) ? macrosPath : null;
    }
}
