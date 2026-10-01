using System.Security.Cryptography;

namespace Shigure;

/// <summary>
/// 将项目插件部署到用户选择的目录及映射的所有正在运行的游戏目录。项目目录始终是权威源。
/// </summary>
internal sealed class FuyutsuiAddonSyncService
{
    private static readonly string[] InterfaceIconNames =
    [
        "Ability_Rogue_Garrote.tga",
        "INV12_Ability_Rogue_Garrote_Empowered.tga",
    ];
    private readonly string _sourceRoot;
    private readonly WowProcessLocator _processLocator;
    private readonly IReadOnlyList<string> _processNames;
    private readonly string _addonName;
    private readonly string? _selectedExecutablePath;
    private readonly bool _deployInterfaceIcons;

    public FuyutsuiAddonSyncService(
        string sourceRoot, WowProcessLocator processLocator, IReadOnlyList<string> processNames,
        string? selectedExecutablePath, bool deployInterfaceIcons)
    {
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _processLocator = processLocator;
        _processNames = processNames;
        _addonName = Path.GetFileName(_sourceRoot);
        _selectedExecutablePath = selectedExecutablePath;
        _deployInterfaceIcons = deployInterfaceIcons;
    }

    public string SourceRoot => _sourceRoot;

    public FuyutsuiAddonSyncResult SynchronizeAll()
    {
        if (!Directory.Exists(_sourceRoot))
        {
            throw new DirectoryNotFoundException($"找不到项目 {_addonName} 目录: {_sourceRoot}");
        }

        var sourcePaths = Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        return SynchronizeTargets(sourcePaths, includeInterfaceIcons: true);
    }

    private FuyutsuiAddonSyncResult SynchronizeTargets(
        IReadOnlyList<string> sourcePaths, bool includeInterfaceIcons = false)
    {
        var (targets, warnings) = ResolveTargetRoots();
        if (targets.Count == 0) return FuyutsuiAddonSyncResult.TargetNotFound(_sourceRoot, warnings);

        var results = new List<FuyutsuiAddonSyncTargetResult>();
        var iconsUpdated = false;
        foreach (var targetRoot in targets)
        {
            var copied = new List<string>();
            var skipped = new List<string>();
            var failures = new List<FuyutsuiAddonSyncFailure>();
            foreach (var sourcePath in sourcePaths)
            {
                var relativePath = Path.GetRelativePath(_sourceRoot, sourcePath);
                SynchronizeCore(sourcePath, relativePath, targetRoot, copied, skipped, failures);
            }
            if (includeInterfaceIcons && _deployInterfaceIcons)
            {
                // 正式服纹理位于插件项目的同级 Interface，部署到游戏 Interface 而非 AddOns。
                var interfaceRoot = Path.GetDirectoryName(Path.GetDirectoryName(targetRoot))!;
                var iconSourceRoot = Path.Combine(Path.GetDirectoryName(_sourceRoot)!, "Interface", "ICONS");
                var copiedBeforeIcons = copied.Count;
                foreach (var name in InterfaceIconNames)
                {
                    SynchronizeCore(Path.Combine(iconSourceRoot, name), Path.Combine("ICONS", name),
                        interfaceRoot, copied, skipped, failures, backupExisting: true);
                }
                iconsUpdated |= copied.Count > copiedBeforeIcons;
            }
            results.Add(new FuyutsuiAddonSyncTargetResult(targetRoot, copied, skipped, failures));
        }
        return new FuyutsuiAddonSyncResult(_sourceRoot, results, null, warnings)
        {
            Notices = iconsUpdated
                ? ["锁喉图标纹理已更新；请完全退出并重新启动游戏，/reload 无法保证加载新纹理。"]
                : [],
        };
    }

    public FuyutsuiAddonSyncResult SynchronizeFile(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var relativePath = Path.GetRelativePath(_sourceRoot, fullSourcePath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"待同步文件不在项目 {_addonName} 目录内: {fullSourcePath}");
        }

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到待同步的项目插件文件。", fullSourcePath);
        }

        return SynchronizeTargets([fullSourcePath]);
    }

    private (IReadOnlyList<string> Targets, IReadOnlyList<string> Warnings) ResolveTargetRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(_selectedExecutablePath))
        {
            if (GameExecutablePath.TryValidate(_selectedExecutablePath,
                    out var selectedPath, out var error))
            {
                var selectedAddOnsDirectory = WowAddonLocator.FindAddOnsDirectoryFromProcessPath(selectedPath);
                if (!string.IsNullOrWhiteSpace(selectedAddOnsDirectory))
                {
                    roots.Add(Path.Combine(selectedAddOnsDirectory, _addonName));
                }
            }
            else
            {
                warnings.Add($"已保存的游戏程序路径不可用：{error}");
            }
        }

        foreach (var processName in _processNames)
        {
            foreach (var processPath in _processLocator.FindRunningProcessPaths(processName))
            {
                var addOnsDirectory = WowAddonLocator.FindAddOnsDirectoryFromProcessPath(processPath);
                if (!string.IsNullOrWhiteSpace(addOnsDirectory))
                {
                    roots.Add(Path.Combine(addOnsDirectory, _addonName));
                }
            }
        }
        return (roots.ToArray(), warnings);
    }

    private static void SynchronizeCore(
        string sourcePath,
        string relativePath,
        string targetRoot,
        ICollection<string> copied,
        ICollection<string> skipped,
        ICollection<FuyutsuiAddonSyncFailure> failures,
        bool backupExisting = false)
    {
        var reportedPath = backupExisting ? Path.Combine("Interface", relativePath) : relativePath;
        try
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("项目源文件缺失，未修改游戏文件。", sourcePath);
            }
            var targetPath = Path.Combine(targetRoot, relativePath);
            if (File.Exists(targetPath) && FilesHaveSameHash(sourcePath, targetPath))
            {
                skipped.Add(reportedPath);
                return;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (backupExisting)
            {
                // 第一次覆盖时保留原纹理；后续更新不得覆盖最初备份。
                var backupPath = targetPath + ".shigure-original";
                if (File.Exists(targetPath) && !File.Exists(backupPath))
                {
                    File.Copy(targetPath, backupPath, overwrite: false);
                }
                var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(sourcePath, temporaryPath, overwrite: false);
                    File.Move(temporaryPath, targetPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            copied.Add(reportedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            failures.Add(new FuyutsuiAddonSyncFailure(reportedPath, ex.Message));
        }
    }

    private static bool FilesHaveSameHash(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        var firstHash = SHA256.HashData(first);
        var secondHash = SHA256.HashData(second);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }
}

internal sealed record FuyutsuiAddonSyncFailure(string RelativePath, string Message);

internal sealed record FuyutsuiAddonSyncTargetResult(
    string TargetRoot,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<FuyutsuiAddonSyncFailure> Failures);

internal sealed record FuyutsuiAddonSyncResult(
    string SourceRoot,
    IReadOnlyList<FuyutsuiAddonSyncTargetResult> Targets,
    string? SkippedReason,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> Notices { get; init; } = [];
    public bool TargetFound => Targets.Count > 0;
    public string? TargetRoot => TargetFound
        ? string.Join("；", Targets.Select(target => target.TargetRoot)) : null;
    public IReadOnlyList<string> CopiedFiles => Targets.SelectMany(target => target.CopiedFiles).ToArray();
    public IReadOnlyList<string> SkippedFiles => Targets.SelectMany(target => target.SkippedFiles).ToArray();
    public IReadOnlyList<FuyutsuiAddonSyncFailure> Failures => Targets.SelectMany(target =>
        target.Failures.Select(failure => new FuyutsuiAddonSyncFailure(
            $"{target.TargetRoot}: {failure.RelativePath}", failure.Message))).ToArray();
    public bool CompletedSuccessfully => TargetFound && Failures.Count == 0 && Warnings.Count == 0;

    public static FuyutsuiAddonSyncResult TargetNotFound(
        string sourceRoot, IReadOnlyList<string> warnings) => new(
        sourceRoot,
        [],
        "未找到可用的游戏插件目录，已跳过游戏插件同步。",
        warnings);
}
