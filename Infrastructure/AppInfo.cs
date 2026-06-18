using System.Reflection;

namespace Shigure;

/// <summary>
/// 应用版本信息的单一来源。优先取 csproj 的 &lt;Version&gt;(AssemblyInformationalVersion),
/// 退化到 AssemblyVersion; 随机副本运行时同样从拷贝出的程序集读取, 版本属性保留。
/// </summary>
internal static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 去掉构建元数据后缀(如 "1.1.0+abc123")。
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "未知";
    }
}
