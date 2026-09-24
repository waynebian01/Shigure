namespace Shigure;

internal interface IShigureRuntimeFactory
{
    ShigureRuntime Create(AppOptions options);
}

internal sealed class ShigureRuntimeFactory : IShigureRuntimeFactory
{
    private readonly string _baseDirectory;
    private readonly ModuleStore _moduleStore;
    private readonly ITriggerKeyState _triggerKeyState;
    private readonly WowProcessLocator _processLocator;
    private readonly TimeProvider _timeProvider;

    public ShigureRuntimeFactory(
        string baseDirectory,
        ModuleStore moduleStore,
        ITriggerKeyState triggerKeyState,
        WowProcessLocator processLocator,
        TimeProvider? timeProvider = null)
    {
        _baseDirectory = baseDirectory;
        _moduleStore = moduleStore;
        _triggerKeyState = triggerKeyState;
        _processLocator = processLocator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ShigureRuntime Create(AppOptions options)
    {
        // 模块目录由启动/刷新时的依赖导入流程统一重载并过滤；这里直接使用已验证快照，
        // 避免把因宏容量超限而拒绝的磁盘模块重新带回运行时。
        var config = ConfigService.LoadFromBaseDirectory(_baseDirectory);
        var keymap = new KeymapService(_baseDirectory, config);

        return new ShigureRuntime(
            options,
            options.CaptureMethod == CaptureMethod.ScreenCopy
                ? new PixelScanner(_processLocator)
                : new WindowsGraphicsCaptureScanner(_processLocator, options.LogicInterval),
            new StateBuilder(config),
            new KeySender(_processLocator),
            _triggerKeyState,
            new LogicRegistry(
                keymap,
                _moduleStore,
                options.ModuleId,
                defaultModules: UiCacheStore.Load().DefaultModules),
            _timeProvider);
    }
}
