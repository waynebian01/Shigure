using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Shigure;

internal sealed record LocatedGameWindow(nint WindowHandle, GameExecutableBinding Binding)
{
    public GameProfile Profile => Binding.Profile;
    public string ExecutablePath => Binding.ExecutablePath;
}

/// <summary>
/// 按所选 exe 的完整路径识别版本，从 Windows Z 顺序顶部查找第一个匹配的可见顶层窗口。
/// 绑定的运行时只接受创建时的版本和路径；修改路径后旧实例立即失去目标。
/// </summary>
internal sealed class WowProcessLocator
{
    private readonly GameExecutableBindings _bindings;
    private readonly GameProfile? _boundProfile;
    private readonly string? _boundExecutablePath;

    public WowProcessLocator(GameExecutableBindings bindings)
    {
        _bindings = bindings;
    }

    private WowProcessLocator(GameExecutableBindings bindings, GameProfile profile, string? executablePath)
    {
        _bindings = bindings;
        _boundProfile = profile;
        _boundExecutablePath = executablePath;
    }

    public WowProcessLocator ForProfile(GameProfile profile, string? executablePath)
        => new(_bindings, profile, executablePath);

    public nint FindFrontmostWindow() => FindFrontmostGame()?.WindowHandle ?? 0;

    public string? FindFrontmostProcessPath() => FindFrontmostGame()?.ExecutablePath;

    public LocatedGameWindow? FindFrontmostGame()
    {
        if (_boundProfile is not null && (_boundExecutablePath is null
            || !_bindings.IsCurrent(new GameExecutableBinding(_boundProfile, _boundExecutablePath))))
            return null;

        // 所有版本一起排序，防止旧版本在切换间隙继续操作后方窗口。
        var candidates = GetCandidateProcesses();
        if (candidates.Count == 0) return null;

        LocatedGameWindow? found = null;
        _ = NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (!candidates.TryGetValue(processId, out var binding)) return true;
            // 枚举后进程可能退出或 PID 被复用，最终窗口仍需核对路径。
            if (!GameExecutablePath.Equals(TryGetProcessPath(processId), binding.ExecutablePath)) return true;
            found = new LocatedGameWindow(hwnd, binding);
            return false;
        }, 0);

        if (found is null || !_bindings.IsCurrent(found.Binding)) return null;
        if (_boundProfile is not null && (
            !found.Profile.Version.Equals(_boundProfile.Version, StringComparison.OrdinalIgnoreCase)
            || !GameExecutablePath.Equals(found.ExecutablePath, _boundExecutablePath))) return null;
        return found;
    }

    public string DescribeConfiguredPaths()
    {
        if (_bindings.ConflictError is { } conflict) return conflict;
        var bindings = _bindings.ReadValidBindings()
            .Where(binding => _boundProfile is null
                || binding.Profile.Version.Equals(_boundProfile.Version, StringComparison.OrdinalIgnoreCase));
        var descriptions = bindings.Select(binding => $"{binding.Profile.Version}: {binding.ExecutablePath}").ToArray();
        return descriptions.Length == 0 ? "请在设置 → 通用中选择有效的游戏程序路径" : string.Join("；", descriptions);
    }

    private Dictionary<uint, GameExecutableBinding> GetCandidateProcesses()
    {
        var bindings = _bindings.ReadValidBindings();
        var result = new Dictionary<uint, GameExecutableBinding>();
        foreach (var processName in bindings.Select(binding => Path.GetFileNameWithoutExtension(binding.ExecutablePath))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] processes;
            try
            {
                // 文件名仅缩小枚举范围，不能用于决定版本。
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        var processId = unchecked((uint)process.Id);
                        var processPath = TryGetProcessPath(processId);
                        var binding = bindings.FirstOrDefault(item =>
                            GameExecutablePath.Equals(item.ExecutablePath, processPath));
                        if (binding is not null) result[processId] = binding;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        // 进程退出或无权读取时跳过，不按文件名回退。
                    }
                }
            }
        }
        return result;
    }

    private static string? TryGetProcessPath(uint processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == 0) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            var size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
                ? buffer.ToString()
                : null;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
