using System.Text.Json;

namespace Shigure;

internal sealed record GameProfile(string AddonName, string Version, string BaseDirectory)
{
    public string RuntimeDirectory => Path.Combine(BaseDirectory, Version);
    public string AddonRoot => Path.Combine(RuntimeDirectory, AddonName);
    public string ModuleDirectory => AddonName.Equals("Fuyutsui", StringComparison.OrdinalIgnoreCase)
        ? ModuleStore.ResolveModuleDirectory()
        : Path.Combine(AppPaths.UserDataDirectory, "module-" + AddonName);
}

internal sealed class GameProfiles
{
    private sealed record ProfileEntry(string Addon, string? Version);
    private sealed record ProfileFile(ProfileEntry[] Profiles);

    private readonly Dictionary<string, GameProfile> _byVersion;
    private readonly GameProfile _default;

    private GameProfiles(Dictionary<string, GameProfile> byVersion, GameProfile defaultProfile)
    {
        _byVersion = byVersion;
        _default = defaultProfile;
    }

    public GameProfile Default => _default;
    public IReadOnlyList<GameProfile> DistinctAddons => _byVersion.Values
        .DistinctBy(profile => profile.AddonName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public GameProfile? FindByVersion(string? version)
        => version is not null && _byVersion.TryGetValue(version, out var profile) ? profile : null;

    public GameProfile? FindByAddon(string? addonName)
        => addonName is null
            ? null
            : _byVersion.Values.FirstOrDefault(profile =>
                string.Equals(profile.AddonName, addonName, StringComparison.OrdinalIgnoreCase));

    public static GameProfiles Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "game_profiles.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到游戏配置 game_profiles.json。", path);
        }

        var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        });
        return Create(baseDirectory, file?.Profiles ?? []);
    }

    private static GameProfiles Create(string baseDirectory, IEnumerable<ProfileEntry> entries)
    {
        var byVersion = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var addon = entry.Addon?.Trim() ?? string.Empty;
            var version = entry.Version?.Trim();
            version ??= addon.Equals("Shingen", StringComparison.OrdinalIgnoreCase) ? "Forever" : "Retail";
            if (version.Equals("Retail", StringComparison.OrdinalIgnoreCase))
            {
                version = "Retail";
            }
            else if (version.Equals("Forever", StringComparison.OrdinalIgnoreCase))
            {
                version = "Forever";
            }
            if (addon.Length == 0
                || addon.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || addon is "." or ".."
                || version is not ("Retail" or "Forever")
                || byVersion.Values.Any(profile =>
                    string.Equals(profile.AddonName, addon, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(profile.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("game_profiles.json 包含无效的版本/插件名称或重复的插件映射。");
            }
            var profile = new GameProfile(addon, version, baseDirectory);
            if (byVersion.TryGetValue(version, out var existing))
            {
                // 旧配置允许同一版本按多个进程重复列出；合并完全相同的版本映射。
                if (string.Equals(existing.AddonName, addon, StringComparison.OrdinalIgnoreCase)) continue;
                throw new InvalidDataException("game_profiles.json 的同一版本不能映射到不同插件。");
            }
            byVersion.Add(version, profile);
        }
        if (byVersion.Count == 0)
        {
            throw new InvalidDataException("game_profiles.json 未配置游戏版本。");
        }
        var defaultProfile = byVersion.GetValueOrDefault("Retail") ?? byVersion.Values.First();
        return new GameProfiles(byVersion, defaultProfile);
    }
}

internal sealed class ActiveGameProfile(GameProfile profile, string? executablePath = null)
{
    public GameProfile Current { get; set; } = profile;
    public string? ExecutablePath { get; set; } = executablePath;
}
