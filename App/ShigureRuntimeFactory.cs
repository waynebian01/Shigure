namespace Shigure;

internal interface IShigureRuntimeFactory
{
    ShigureRuntime Create(AppOptions options);
}

internal sealed class ShigureRuntimeFactory : IShigureRuntimeFactory
{
    private readonly Func<GameProfile, ModuleStore> _resolveModuleStore;
    private readonly ITriggerKeyState _triggerKeyState;
    private readonly WowProcessLocator _processLocator;
    private readonly ActiveGameProfile _activeProfile;
    private readonly TimeProvider _timeProvider;

    public ShigureRuntimeFactory(
        Func<GameProfile, ModuleStore> resolveModuleStore,
        ITriggerKeyState triggerKeyState,
        WowProcessLocator processLocator,
        ActiveGameProfile activeProfile,
        TimeProvider? timeProvider = null)
    {
        _resolveModuleStore = resolveModuleStore;
        _triggerKeyState = triggerKeyState;
        _processLocator = processLocator;
        _activeProfile = activeProfile;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ShigureRuntime Create(AppOptions options)
    {
        // 模块目录由启动/刷新时的依赖导入流程统一重载并过滤；这里直接使用已验证快照，
        // 避免把因宏容量超限而拒绝的磁盘模块重新带回运行时。
        var profile = _activeProfile.Current;
        var config = ConfigService.LoadFromBaseDirectory(profile.RuntimeDirectory);
        var keymap = new KeymapService(profile.RuntimeDirectory, config, profile.AddonRoot);
        var processLocator = _processLocator.ForProfile(profile, _activeProfile.ExecutablePath);

        return new ShigureRuntime(
            options,
            options.CaptureMethod == CaptureMethod.ScreenCopy
                ? new PixelScanner(processLocator)
                : new WindowsGraphicsCaptureScanner(processLocator, options.ScanInterval),
            new StateBuilder(config),
            new KeySender(processLocator),
            _triggerKeyState,
            new LogicRegistry(
                keymap,
                _resolveModuleStore(profile),
                options.ModuleId,
                defaultModules: UiCacheStore.Load().DefaultModules),
            _timeProvider);
    }
}
