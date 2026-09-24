using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Shigure;

internal enum BorderlessCaptureAccessState
{
    Allowed,
    Denied,
    Unsupported,
    PackageIdentityRequired,
    Failed
}

internal sealed record BorderlessCaptureAccessResult(
    BorderlessCaptureAccessState State,
    string Message);

internal static class BorderlessCaptureAccess
{
    private const int ErrorInsufficientBuffer = 122;

    public static async Task<BorderlessCaptureAccessResult> RequestAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            return new BorderlessCaptureAccessResult(
                BorderlessCaptureAccessState.Unsupported,
                "当前 Windows 版本不支持关闭 WGC 捕获边框");
        }

        if (!HasPackageIdentity())
        {
            return new BorderlessCaptureAccessResult(
                BorderlessCaptureAccessState.PackageIdentityRequired,
                "当前程序没有包身份，WGC 将尝试关闭边框，但系统仍可能显示黄色提示框");
        }

        try
        {
            var status = await GraphicsCaptureAccess.RequestAccessAsync(
                GraphicsCaptureAccessKind.Borderless);
            return status switch
            {
                AppCapabilityAccessStatus.Allowed => new BorderlessCaptureAccessResult(
                    BorderlessCaptureAccessState.Allowed,
                    "已获得 Windows 无边框捕获授权"),
                AppCapabilityAccessStatus.DeniedByUser => new BorderlessCaptureAccessResult(
                    BorderlessCaptureAccessState.Denied,
                    "用户未授权无边框捕获，WGC 将保留系统黄色提示框"),
                AppCapabilityAccessStatus.DeniedBySystem => new BorderlessCaptureAccessResult(
                    BorderlessCaptureAccessState.Denied,
                    "系统拒绝无边框捕获授权，WGC 将保留系统黄色提示框"),
                _ => new BorderlessCaptureAccessResult(
                    BorderlessCaptureAccessState.Denied,
                    $"无边框捕获授权状态为 {status}，WGC 将保留系统黄色提示框")
            };
        }
        catch (Exception ex)
        {
            return new BorderlessCaptureAccessResult(
                BorderlessCaptureAccessState.Failed,
                $"请求无边框捕获授权失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        [Out] char[]? packageFullName);
}
