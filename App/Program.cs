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
            if (!Directory.Exists(Path.Combine(baseDirectory, "Fuyutsui", "class")))
            {
                baseDirectory = AppPaths.BaseDirectory;
            }
            FuyutsuiConfigConverter.UpdateFromClassDirectory(
                Path.Combine(baseDirectory, "Fuyutsui", "class"),
                Path.Combine(baseDirectory, ConfigService.ConfigDirectoryName));
            return;
        }

        if (args.Contains("--update-keymap", StringComparer.OrdinalIgnoreCase))
        {
            var baseDirectory = Directory.GetCurrentDirectory();
            if (!Directory.Exists(Path.Combine(baseDirectory, "Fuyutsui", "core")))
            {
                baseDirectory = AppPaths.BaseDirectory;
            }

            FuyutsuiKeymapConverter.UpdateFromClassMacros(
                Path.Combine(baseDirectory, "Fuyutsui", "core", "classmacros.lua"),
                Path.Combine(baseDirectory, "keymap"));
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
            var moduleStore = new ModuleStore(ModuleStore.ResolveModuleDirectory());
            ShowModuleMigrationHint(baseDirectory, moduleStore);
            var triggerKeyState = new WindowsTriggerKeyState();
            var processLocator = new WowProcessLocator(baseDirectory);
            var runtimeFactory = new ShigureRuntimeFactory(baseDirectory, moduleStore, triggerKeyState, processLocator);
            var runtimeSession = new RuntimeSessionCoordinator(runtimeFactory);

            Application.Run(new MainForm(
                options,
                baseDirectory,
                moduleStore,
                triggerKeyState,
                processLocator,
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
