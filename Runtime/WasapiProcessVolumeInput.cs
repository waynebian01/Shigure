using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Shigure;

// 读取进程音频会话的峰值表，不读取音量滑块，也不使用设备的混合输出峰值。
internal sealed class WasapiProcessVolumeInput(WowProcessLocator processLocator) : IRuntimeVolumeInput
{
    public VolumePeakSample ReadPeak(nint expectedWindow)
    {
        if (expectedWindow == 0 || processLocator.FindFrontmostWindow() != expectedWindow)
            return new VolumePeakSample(null, "等待重新扫描目标游戏窗口");
        if (NativeMethods.GetWindowThreadProcessId(expectedWindow, out var processId) == 0 || processId == 0)
            return new VolumePeakSample(null, "无法读取目标游戏进程");
        return ReadProcessPeak(processId);
    }

    internal static VolumePeakSample ReadProcessPeak(uint processId)
    {
        if (processId == 0) return new VolumePeakSample(null, "目标进程无效");
        double peak = 0;
        var targetRead = false;
        string? failure = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            // 目标可能通过非默认设备出声；枚举所有活动播放设备，按 PID 过滤会话。
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (var i = 0; i < sessions.Count; i++)
                        {
                            try
                            {
                                using var session = sessions[i];
                                if (session.GetProcessID != processId) continue;
                                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                                if (session.State == AudioSessionState.AudioSessionStateInactive)
                                {
                                    targetRead = true;
                                    continue;
                                }
                                var meter = session.AudioMeterInformation;
                                if (meter is null)
                                {
                                    failure = "目标音频会话不支持峰值读取";
                                    continue;
                                }
                                var value = meter.MasterPeakValue;
                                if (!float.IsFinite(value))
                                {
                                    failure = "目标音频会话返回了无效峰值";
                                    continue;
                                }
                                peak = Math.Max(peak, Math.Clamp((double)value, 0, 1));
                                targetRead = true;
                            }
                            catch (Exception ex) when (ex is COMException or InvalidOperationException)
                            {
                                failure = ex.Message;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is COMException or InvalidOperationException)
                    {
                        failure = ex.Message;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return new VolumePeakSample(null, $"音量识别不可用：{ex.Message}");
        }
        // 无会话或未播放时为 0；设备读取失败时保留“不可用”，避免误命中 <= 0 等条件。
        return failure is not null && !targetRead
            ? new VolumePeakSample(null, $"音量识别不可用：{failure}")
            : new VolumePeakSample(peak * 100);
    }
}
