namespace Shigure;

internal sealed record GameExecutableBinding(GameProfile Profile, string ExecutablePath);

/// <summary>运行目标和插件部署共用的路径映射；更新时一次替换完整快照。</summary>
internal sealed class GameExecutableBindings(GameProfiles profiles)
{
    private sealed record Selection(string? RetailPath, string? ForeverPath, string? ConflictError);

    private Selection _selection = new(null, null, null);

    public string? ConflictError => Volatile.Read(ref _selection).ConflictError;

    public void Update(string? retailPath, string? foreverPath)
    {
        var conflict = GameExecutablePath.Equals(retailPath, foreverPath)
            ? "正式服和无限服选择了同一游戏程序。请为两个版本选择不同路径；版本识别和插件部署已暂停。"
            : null;
        Volatile.Write(ref _selection, new Selection(retailPath, foreverPath, conflict));
    }

    public string? GetSelectedPath(GameProfile profile)
        => GetSelectedPath(Volatile.Read(ref _selection), profile);

    public IReadOnlyList<GameExecutableBinding> ReadValidBindings()
    {
        var selection = Volatile.Read(ref _selection);
        if (selection.ConflictError is not null) return [];
        var bindings = new List<GameExecutableBinding>();
        foreach (var profile in profiles.DistinctAddons)
        {
            if (GameExecutablePath.TryValidate(GetSelectedPath(selection, profile), out var fullPath, out _))
                bindings.Add(new GameExecutableBinding(profile, fullPath));
        }
        return bindings;
    }

    public bool IsCurrent(GameExecutableBinding binding)
        => ReadValidBindings().Any(current =>
            current.Profile.Version.Equals(binding.Profile.Version, StringComparison.OrdinalIgnoreCase)
            && GameExecutablePath.Equals(current.ExecutablePath, binding.ExecutablePath));

    private static string? GetSelectedPath(Selection selection, GameProfile profile)
        => profile.Version.Equals("Retail", StringComparison.OrdinalIgnoreCase)
            ? selection.RetailPath
            : selection.ForeverPath;
}
