using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Shigure.Installer;

/// <summary>
/// 项目构建器：把主项目源码复制到临时目录后，用命令行覆盖程序集名称发布，最后打包到输出目录。
///
/// 设计要点（旧版「经常出错」的根因都在这里规避）：
/// - 全程在 <c>%TEMP%</c> 的隔离副本里构建，<b>绝不</b>修改主项目的 .csproj / 源码 / bin / obj，
///   也不依赖「备份-还原」这种崩溃即损坏的机制。
/// - 程序集改名通过命令行属性 <c>-p:AssemblyName=...</c> 覆盖，而非改写 .csproj 文本。
/// - 复制时排除 bin / obj / 安装器自身等目录，临时副本天然干净，不会出现重复特性(CS0579)。
/// - 调用进程一律用 <see cref="ProcessStartInfo.ArgumentList"/>，自动处理含空格的路径（仓库路径含 "VS Code"）。
/// </summary>
internal sealed class ProjectBuilder
{
    private readonly Action<string> _log;

    /// <summary>复制源码时按目录名跳过（任意层级），避免拷入构建产物与无关目录。</summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".vscode", "cache", "artifacts", "tmp", "Shigure.Installer", "node_modules"
    };

    /// <summary>输出包中必须存在的「运行时数据」文件夹（publish 已复制内容，此处保底确保目录存在）。</summary>
    private static readonly string[] DataDirectories = { "config", "keymap", "module" };

    /// <summary>合法程序集名称：仅字母/数字/下划线，且不能以数字开头。GUI 与命令行共用此校验。</summary>
    private static readonly Regex NamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public ProjectBuilder(Action<string> log) => _log = log;

    /// <summary>校验自定义程序名称是否合法。</summary>
    public static bool IsValidProgramName(string? name) =>
        !string.IsNullOrEmpty(name) && NamePattern.IsMatch(name);

    /// <summary>
    /// 完整流程：定位主项目 → 复制到临时目录 → 发布(改名) → 打包到输出目录 → 清理临时目录。
    /// </summary>
    /// <param name="programName">自定义程序集名称。</param>
    /// <param name="outputDir">最终输出目录（会被重建）。</param>
    public void BuildAndPackage(string programName, string outputDir)
    {
        if (!IsValidProgramName(programName))
            throw new ArgumentException("程序名称不合法：只能包含英文字母、数字、下划线，且不能以数字开头。", nameof(programName));

        var csprojPath = LocateMainProject();
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        _log($"主项目：{csprojPath}");

        var workRoot = Path.Combine(Path.GetTempPath(), "ShigureInstaller", Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(workRoot, "src");
        var publishDir = Path.Combine(workRoot, "publish");

        try
        {
            _log("① 复制源码到临时目录（排除 bin/obj 等）…");
            CopyDirectory(projectDir, srcDir);

            _log($"② 发布并改名为 {programName}（dotnet publish）…");
            Publish(Path.Combine(srcDir, Path.GetFileName(csprojPath)), publishDir, programName);

            _log("③ 打包到输出目录…");
            PackageOutput(publishDir, outputDir);
        }
        finally
        {
            _log("④ 清理临时文件…");
            TryDeleteDirectory(workRoot);
        }
    }

    /// <summary>
    /// 从安装器所在位置逐级向上查找主项目 <c>Shigure.csproj</c>。
    /// 安装器自身是 <c>Shigure.Installer.csproj</c>，不会被误匹配。
    /// </summary>
    private static string LocateMainProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Shigure.csproj");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "未能从安装器所在位置向上找到主项目 Shigure.csproj。\n" +
            "请确认安装器位于 Shigure 仓库的 Shigure.Installer 子目录内运行。");
    }

    /// <summary>递归复制目录，跳过 <see cref="ExcludedDirectories"/> 以及 *.backup / *.user 文件。</summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (ExcludedDirectories.Contains(name))
                continue;

            CopyDirectory(subDir, Path.Combine(destDir, name));
        }
    }

    /// <summary>在临时副本上执行 <c>dotnet publish</c>，用 <c>-p:AssemblyName</c> 覆盖程序集名称。</summary>
    private void Publish(string csprojPath, string publishDir, string programName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(csprojPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(publishDir);
        startInfo.ArgumentList.Add($"-p:AssemblyName={programName}");
        startInfo.ArgumentList.Add("--nologo");

        using var process = new Process { StartInfo = startInfo };

        // 收集疑似错误行，构建失败时回传给用户，避免日志被淹没后看不到原因。
        var errorLines = new List<string>();

        void Handle(string? line, bool isError)
        {
            if (string.IsNullOrEmpty(line))
                return;

            var looksLikeError = isError ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("错误");

            if (looksLikeError && errorLines.Count < 20)
                errorLines.Add(line);

            _log((isError ? "  ! " : "    ") + line);
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => Handle(e.Data, isError: true);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "无法启动 dotnet。请确认已安装 .NET 10 SDK 且 dotnet 在 PATH 中。\n" + ex.Message, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var detail = errorLines.Count > 0
                ? "\n\n" + string.Join("\n", errorLines)
                : string.Empty;
            throw new InvalidOperationException($"dotnet publish 失败（退出码 {process.ExitCode}）。{detail}");
        }
    }

    /// <summary>把发布产物复制到输出目录，保留 config/keymap/module 内容。</summary>
    private void PackageOutput(string publishDir, string outputDir)
    {
        if (Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"无法清理已存在的输出目录：{outputDir}\n" +
                    "可能有文件被占用（如该程序正在运行，或文件夹在资源管理器中打开）。请关闭后重试。", ex);
            }
        }

        CopyDirectory(publishDir, outputDir);

        foreach (var name in DataDirectories)
            Directory.CreateDirectory(Path.Combine(outputDir, name));

        _log("已包含 config/keymap/module 配置内容");
    }

    /// <summary>尽力删除临时目录；失败不抛出（临时文件残留无伤大雅）。</summary>
    private void TryDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _log($"  （提示）临时目录未能完全清理：{dir}（{ex.Message}）");
        }
    }
}
