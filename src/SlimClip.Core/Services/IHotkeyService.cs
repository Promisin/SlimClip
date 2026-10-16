using System;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    bool Register(HotkeyConfig config);
    void Unregister();

    // Hotkey recording support
    event EventHandler<HotkeyConfig>? HotkeyRecorded;
    void StartRecording();
    void StopRecording();
}
