namespace Shigure;

internal sealed record SpellSuggestion(long SpellId, string Name);
internal sealed record ItemSuggestion(long ItemId, string Name);

/// <summary>管理一个外置包；编辑和运行界面显式选择各自版本的目录。</summary>
internal static class SpellIconCatalog
{
    internal static readonly object SyncRoot = new();
    private static readonly Dictionary<string, VersionedSpellIconCatalog> Catalogs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Retail"] = new("Retail"),
        ["Forever"] = new("Forever")
    };
    internal static GameDataPackage? Package { get; private set; }
    internal static event Action? CatalogChanged;
    internal static string PackagePath => Path.Combine(AppContext.BaseDirectory, "data", "SpellIcons.shgpack");
    internal static string PendingPackagePath => $"{PackagePath}.pending";
    internal static bool IsPackageAvailable { get { lock (SyncRoot) return Package is not null; } }
    internal static bool HasForever { get { lock (SyncRoot) return Package?.HasForever == true; } }

    static SpellIconCatalog()
    {
        PromotePendingPackage();
        Package = GameDataPackage.TryOpen(PackagePath, verifyIcons: true);
        RebuildIndexesLocked();
    }

    internal static VersionedSpellIconCatalog ForVersion(string version)
        => Catalogs.TryGetValue(version, out var catalog) ? catalog : throw new ArgumentException("未知的数据版本。", nameof(version));

    private static void PromotePendingPackage()
    {
        if (!File.Exists(PendingPackagePath)) return;
        try
        {
            using var pending = GameDataPackage.Open(PendingPackagePath);
            pending.VerifyIconHashes();
            using var current = GameDataPackage.TryOpen(PackagePath);
            if (current?.HasForever == true && !pending.HasForever) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PackagePath)!);
            pending.Dispose();
            current?.Dispose();
            File.Move(PendingPackagePath, PackagePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or System.Text.Json.JsonException) { }
    }

    internal static void ValidatePackage(string path)
    {
        using var package = GameDataPackage.Open(path);
        package.VerifyIconHashes();
    }

    internal static void InstallPackage(string downloadedPath)
        => InstallPackage(downloadedPath, CancellationToken.None);

    internal static void InstallPackage(string downloadedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var candidate = GameDataPackage.Open(downloadedPath))
        {
            candidate.VerifyIconHashes();
            lock (SyncRoot)
            {
                if (Package?.HasForever == true && !candidate.HasForever)
                    throw new InvalidDataException("远端数据包不包含 Forever，已保留本地两版数据包。");
            }
        }
        var backup = $"{PackagePath}.{Guid.NewGuid():N}.backup";
        Exception? failure = null;
        lock (SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existed = File.Exists(PackagePath);
            Package?.Dispose();
            Package = null;
            foreach (var catalog in Catalogs.Values) catalog.DisposeImageCachesLocked();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PackagePath)!);
                if (existed) File.Replace(downloadedPath, PackagePath, backup, ignoreMetadataErrors: true);
                else File.Move(downloadedPath, PackagePath);
                Package = GameDataPackage.Open(PackagePath);
            }
            catch (Exception error)
            {
                failure = error;
                try
                {
                    if (existed && File.Exists(backup)) File.Move(backup, PackagePath, overwrite: true);
                    else if (!existed) TryDeleteFile(PackagePath);
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { }
                Package = GameDataPackage.TryOpen(PackagePath);
            }
            RebuildIndexesLocked();
        }
        if (failure is null) TryDeleteFile(backup);
        CatalogChanged?.Invoke();
        if (failure is not null) throw new IOException("安装技能/物品数据包失败，已尝试恢复原包。", failure);
    }

    private static void RebuildIndexesLocked()
    {
        foreach (var catalog in Catalogs.Values) catalog.RebuildIndexesLocked();
    }
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
