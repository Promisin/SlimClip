using System;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipboardData>? DataCopied;
    void Start();
    void Stop();
    void SuppressNextCapture();
    bool IsSuppressed { get; }
}
