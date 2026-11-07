using System;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public enum HotkeyRegistrationStatus
{
    /// <summary>成功通过原生接口注册，或快捷键已显式禁用（无）。</summary>
    Success = 0,

    /// <summary>含 Win 键的字母/数字快捷键被系统占用，可通过开启“允许禁用系统快捷键”接管。</summary>
    OccupiedCanOverrideSystem = 1,

    /// <summary>已写入 DisabledHotkeys 注册表，需重启 Windows 资源管理器后方可释放并生效。</summary>
    PendingExplorerRestart = 2,

    /// <summary>被系统底层（如 Win+L）或其他应用独占，无法注册。</summary>
    OccupiedCannotRegister = 3
}

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    bool Register(HotkeyConfig config);
    void Unregister();

    HotkeyRegistrationStatus LastRegistrationStatus { get; }

    /// <summary>
    /// 配置是否允许通过注册表禁用系统 Win 快捷键（仅 Windows 有效）。
    /// </summary>
    void ConfigureSystemHotkeyOverride(bool allowOverride, string? initialManagedChar = null, Action<string>? onManagedCharChanged = null);

    /// <summary>
    /// 重启系统外壳（Windows explorer.exe）并重新尝试注册当前快捷键。
    /// </summary>
    bool RestartExplorerAndReRegister();

    // Hotkey recording support
    event EventHandler<HotkeyConfig>? HotkeyRecorded;
    void StartRecording();
    void StopRecording();
}
