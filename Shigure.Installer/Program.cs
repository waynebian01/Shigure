namespace Shigure.Installer;

using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 带参数时走命令行模式（便于脚本化 / 批量生成），无参数时打开图形界面。
        //   Shigure.Installer.exe <程序名> [输出目录]
        if (args.Length > 0)
        {
            // WinExe 默认不连接父控制台，附加后命令行输出才可见。
            AttachConsole(AttachParentProcess);
            return RunHeadless(args);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new InstallerForm());
        return 0;
    }

    private static int RunHeadless(string[] args)
    {
        var positional = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "--help" or "-h" or "/?")
            {
                PrintUsage();
                return 0;
            }

            positional.Add(arg);
        }

        if (positional.Count == 0)
        {
            PrintUsage();
            return 1;
        }

        var programName = positional[0];
        var baseDir = positional.Count > 1
            ? positional[1]
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outputDir = Path.Combine(baseDir, programName);

        try
        {
            var builder = new ProjectBuilder(Console.WriteLine);
            builder.BuildAndPackage(programName, outputDir);
            Console.WriteLine($"\n✓ 构建完成：{outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n✗ 构建失败：{ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法：Shigure.Installer.exe <程序名> [输出目录]");
        Console.WriteLine("  <程序名>      仅字母/数字/下划线，不能以数字开头");
        Console.WriteLine("  [输出目录]    可选，默认桌面；最终输出到 <输出目录>\\<程序名>");
        Console.WriteLine("不带任何参数运行则打开图形界面。");
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}
