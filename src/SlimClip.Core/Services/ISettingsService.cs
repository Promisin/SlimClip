using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Load();
    string GetDataDirectory();
    Task<bool> RequestElevationRestartAsync();
    bool SetAutoStart(bool enabled);
    bool IsAutoStartEnabled();
    bool IsElevated => false;
}
