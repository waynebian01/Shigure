namespace Shigure;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\ArasakaCorporation.Shigure.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--update-config", StringComparer.OrdinalIgnoreCase))
        {
            var baseDirectory = Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(baseDirectory, "game_profiles.json")))
            {
                baseDirectory = AppPaths.BaseDirectory;
            }
            foreach (var profile in GameProfiles.Load(baseDirectory).DistinctAddons)
            {
                FuyutsuiConfigConverter.UpdateFromClassDirectory(
                    Path.Combine(profile.AddonRoot, "class"),
                    Path.Combine(profile.RuntimeDirectory, ConfigService.ConfigDirectoryName));
            }
            return;
        }

        if (args.Contains("--update-keymap", StringComparer.OrdinalIgnoreCase))
        {
            var baseDirectory = Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(baseDirectory, "game_profiles.json")))
            {
                baseDirectory = AppPaths.BaseDirectory;
            }

            foreach (var profile in GameProfiles.Load(baseDirectory).DistinctAddons)
            {
                FuyutsuiKeymapConverter.UpdateFromClassMacros(
                    Path.Combine(profile.AddonRoot, "core", "classmacros.lua"),
                    Path.Combine(profile.RuntimeDirectory, "keymap"));
            }
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show(
                "Shigure 需要在 Windows 上运行。",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);

        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Shigure 已经在运行。",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var options = AppOptions.FromArgs(args);
            var baseDirectory = AppPaths.BaseDirectory;
            var profiles = GameProfiles.Load(baseDirectory);
            var uiCache = UiCacheStore.Load();
            var executableBindings = new GameExecutableBindings(profiles);
            executableBindings.Update(uiCache.RetailGameExecutablePath, uiCache.ForeverGameExecutablePath);
            var processLocator = new WowProcessLocator(executableBindings);
            var foregroundGame = processLocator.FindFrontmostGame();
            var initialProfile = foregroundGame?.Profile ?? profiles.Default;
            var initialPath = foregroundGame?.ExecutablePath ?? executableBindings.ReadValidBindings()
                .FirstOrDefault(binding => binding.Profile.Version == initialProfile.Version)?.ExecutablePath;
            var activeProfile = new ActiveGameProfile(initialProfile, initialPath);
            var workspaces = profiles.DistinctAddons.ToDictionary(
                profile => profile.AddonName,
                profile => new GameWorkspace(profile),
                StringComparer.OrdinalIgnoreCase);
            ShowModuleMigrationHint(baseDirectory, workspaces[initialProfile.AddonName].Modules);
            var triggerKeyState = new WindowsTriggerKeyState();
            var runtimeFactory = new ShigureRuntimeFactory(
                profile => workspaces[profile.AddonName].Modules,
                triggerKeyState, processLocator, activeProfile);
            var runtimeSession = new RuntimeSessionCoordinator(runtimeFactory);

            Application.Run(new MainForm(
                options,
                baseDirectory,
                workspaces,
                triggerKeyState,
                processLocator,
                profiles,
                activeProfile,
                executableBindings,
                uiCache,
                runtimeSession));
        }
        finally
        {
            singleInstanceMutex.ReleaseMutex();
        }
    }

    // 模块目录迁移提示：旧目录(baseDirectory/module)存在且有内容、且新目录(我的文档目录)尚无模块文件时，
    // 弹一次纯文案提示，引导用户手动移动模块；条件不满足或检测异常时静默，不影响启动。
    private static void ShowModuleMigrationHint(string baseDirectory, ModuleStore moduleStore)
    {
        try
        {
            var oldDirectory = Path.Combine(baseDirectory, "module");
            if (!Directory.Exists(oldDirectory)
                || !Directory.EnumerateFileSystemEntries(oldDirectory).Any())
            {
                return;
            }

            // 新目录以是否存在模块文件(.json)判空，避免保存崩溃残留的 .tmp 原子写文件阻断提示。
            if (Directory.EnumerateFiles(moduleStore.ModuleDirectory, "*.json", SearchOption.AllDirectories).Any())
            {
                return;
            }

            MessageBox.Show(
                $"模块目录已迁移到我的文档目录。\n\n" +
                $"新的模块目录：\n{moduleStore.ModuleDirectory}\n\n" +
                $"请将旧目录 {oldDirectory} 中的模块文件手动移动到新目录。",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch
        {
            // 检测或提示异常时静默跳过，避免影响启动。
        }
    }
}
