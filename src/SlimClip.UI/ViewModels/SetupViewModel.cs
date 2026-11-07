using System;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimClip.Core.Helpers;
using SlimClip.Core.Services;

namespace SlimClip.UI.ViewModels;

public enum SetupWizardStage
{
    Configuration = 0,
    Installing = 1,
    Completed = 2,
    Failed = 3
}

public partial class SetupViewModel : ObservableObject
{
    private readonly IWindowsSetupService _setupService;
    private readonly ILocalizationService _localizationService;
    private readonly string _sourceDirectory;
    private readonly bool _isUninstallMode;

    public ILocalizationService L => _localizationService;

    public bool IsUninstallMode => _isUninstallMode;
    public bool IsUpgradeMode { get; }

    /// <summary>仅在全新安装（非覆盖升级、非卸载）时允许编辑安装路径。</summary>
    public bool IsPathEditable => !IsUpgradeMode && !_isUninstallMode;

    /// <summary>仅在全新安装时展示「开机自启动」与「创建桌面快捷方式」勾选框。</summary>
    public bool ShowInstallOptions => !IsUpgradeMode && !_isUninstallMode;

    [ObservableProperty]
    private string _installPath;

    [ObservableProperty]
    private bool _enableAutoStart = true;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationStage))]
    [NotifyPropertyChangedFor(nameof(IsInstallingStage))]
    [NotifyPropertyChangedFor(nameof(IsCompletedStage))]
    [NotifyPropertyChangedFor(nameof(IsFailedStage))]
    private SetupWizardStage _stage = SetupWizardStage.Configuration;

    public bool IsConfigurationStage => Stage == SetupWizardStage.Configuration;
    public bool IsInstallingStage => Stage == SetupWizardStage.Installing;
    public bool IsCompletedStage => Stage == SetupWizardStage.Completed;
    public bool IsFailedStage => Stage == SetupWizardStage.Failed;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string AppVersion { get; }

    public string PrimaryActionText => _isUninstallMode
        ? _localizationService["Setup_Btn_Uninstall"]
        : (IsUpgradeMode ? _localizationService["Setup_Btn_Upgrade"] : _localizationService["Setup_Btn_Install"]);

    public string CompletedTitleText => _isUninstallMode
        ? _localizationService["Setup_Done_UninstallTitle"]
        : (IsUpgradeMode ? _localizationService["Setup_Done_UpgradeTitle"] : _localizationService["Setup_Done_InstallTitle"]);

    public event EventHandler? RequestClose;

    public SetupViewModel(
        IWindowsSetupService setupService,
        string? sourceDirectory = null,
        bool isUninstallMode = false,
        ILocalizationService? localizationService = null)
    {
        _setupService = setupService;
        _isUninstallMode = isUninstallMode;
        _sourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
            ? AppContext.BaseDirectory
            : sourceDirectory;
        _localizationService = localizationService ?? new LocalizationService();

        AppVersion = GetVersionString();

        var existingPath = _setupService.DetectExistingInstallPath();
        if (_isUninstallMode)
        {
            IsUpgradeMode = false;
            _installPath = !string.IsNullOrWhiteSpace(existingPath)
                ? existingPath
                : AppContext.BaseDirectory.TrimEnd('\\', '/');
        }
        else if (!string.IsNullOrWhiteSpace(existingPath))
        {
            IsUpgradeMode = true;
            _installPath = existingPath;
        }
        else
        {
            IsUpgradeMode = false;
            _installPath = InstallerPathHelper.GetDefaultInstallDirectory();
        }
    }

    /// <summary>
    /// 当用户通过文件夹选择器选定路径或手动输入确认后调用，自动补齐 \SlimClip 后缀并填入输入框。
    /// 若处于升级锁定模式，则保持原锁定路径不变。
    /// </summary>
    public void ApplySelectedPath(string? selectedPath)
    {
        if (!IsPathEditable)
            return;

        InstallPath = InstallerPathHelper.NormalizeInstallPath(selectedPath);
    }

    [RelayCommand]
    public async Task ConfirmInstallAsync()
    {
        if (Stage == SetupWizardStage.Installing)
            return;

        // 确认前再次确保全新安装路径经过规范化补齐 \SlimClip 后缀
        if (IsPathEditable)
        {
            InstallPath = InstallerPathHelper.NormalizeInstallPath(InstallPath);
        }

        Stage = SetupWizardStage.Installing;
        ProgressPercent = 0;
        ErrorMessage = string.Empty;

        var progress = new Progress<SetupProgress>(p =>
        {
            ProgressPercent = Math.Clamp(p.Percent, 0, 100);
            StatusText = p.StatusText;
        });

        try
        {
            if (_isUninstallMode)
            {
                await _setupService.UninstallAsync(InstallPath, progress);
            }
            else
            {
                var options = new InstallOptions
                {
                    SourceDirectory = _sourceDirectory,
                    TargetDirectory = InstallPath,
                    IsUpgradeMode = IsUpgradeMode,
                    EnableAutoStart = EnableAutoStart,
                    CreateDesktopShortcut = CreateDesktopShortcut,
                    AppVersion = AppVersion.TrimStart('v')
                };
                await _setupService.InstallAsync(options, progress);
            }

            ProgressPercent = 100;
            Stage = SetupWizardStage.Completed;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Stage = SetupWizardStage.Failed;
        }
    }

    [RelayCommand]
    public void LaunchAndExit()
    {
        if (!_isUninstallMode)
        {
            _setupService.LaunchInstalledApp(InstallPath);
        }
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void RetryConfiguration()
    {
        Stage = SetupWizardStage.Configuration;
    }

    private static string GetVersionString()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(SetupViewModel).Assembly;
        var ver = asm.GetName().Version;
        return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.5";
    }
}
