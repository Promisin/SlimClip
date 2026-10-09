; SlimClip — Inno Setup 7 Fluent 2 现代一键流式安装向导脚本
; 遵循项目设计规范：居中对称金字塔排版、黑白为锚反色交互、Windows 11 原生圆角与 DWM 阴影、自适应高度动态贴合

#define MyAppId "{{67EF0B38-FFC8-4D35-B1E3-FFEC27AE6503}"
#define MyAppName "SlimClip"
#define MyAppPublisher "Promisin"
#define MyAppURL "https://github.com/Promisin/SlimClip"
#define MyAppExeName "SlimClip.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.2"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish-staging\windows-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\releases\" + MyAppVersion
#endif

[Setup]
; 专属全局唯一标识符，升级时自动识别旧版本
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; 路径选择与历史路径继承
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=yes
UsePreviousAppDir=yes

; 开始菜单项
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; 权限模型：使用系统管理员权限安装（以支持全局提权应用兼容与免 UAC 计划任务自启）
PrivilegesRequired=admin

; 启用 Inno Setup 7 Windows 11 现代深浅自适应引擎，禁止多余向导页
WizardStyle=modern dynamic windows11 hidebevels
UsedUserAreasWarning=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=no

; 静默探测系统语言，杜绝弹出古老的多余语言选择框
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

; 图标与资源
SetupIconFile=..\src\SlimClip.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; 高性能 lzma2 固实压缩
Compression=lzma2/ultra64
SolidCompression=yes

; 架构与输出配置
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SlimClip-v{#MyAppVersion}-windows-x64-setup
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; 内置深浅色双模式预合成高清位图资源（严格对齐 Windows 11 真实窗口背景）
Source: "app_logo_dark.bmp"; Flags: dontcopy
Source: "app_logo_light.bmp"; Flags: dontcopy
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Check: ShouldCreateDesktopIcon

[Registry]
; 兼容清理旧版本可能残留的 Run 注册表键
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#MyAppName}"; Flags: deletevalue uninsdeletevalue

[Run]
Filename: "schtasks.exe"; Parameters: "/create /tn ""{#MyAppName}"" /tr """"{app}\{#MyAppExeName}"""" /sc onlogon /rl highest /f"; Flags: runhidden; Check: ShouldAutoStart
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/delete /tn ""{#MyAppName}"" /f"; Flags: runhidden; RunOnceId: "DelAutoStartTask"

[Code]
// ==============================================================================
// Win32 / DWM 原生 API 声明
// ==============================================================================
const
  WM_CLOSE = $0010;

function SendMessage(hWnd: HWND; Msg: UINT; wParam: LongInt; lParam: LongInt): LongInt;
  external 'SendMessageW@user32.dll stdcall';

function FindWindow(lpClassName, lpWindowName: String): HWND;
  external 'FindWindowW@user32.dll stdcall';

// ==============================================================================
// 全局界面组件与主题状态
// ==============================================================================
var
  IsDark: Boolean;
  IsUpdateMode: Boolean;
  IsCustomExpanded: Boolean;
  InstalledAppPath: String;

  // 颜色令牌 (RGB)
  ColorBg, ColorTextPrimary, ColorTextSecondary, ColorTextMuted: TColor;
  ColorInputBg, ColorInputBorder: TColor;

  // 容器组件
  MainCard: TPanel;

  // Hero 品牌展示
  LogoImg: TBitmapImage;
  LblTitle, LblSlogan, LblVersion: TLabel;

  // 实体圆角主安装大按钮 (TNewButton 原生支持 Windows 11 现代圆角与动效)
  BtnInstall: TNewButton;

  // 自定义展开按钮与抽屉
  CustomToggleLabel: TLabel;
  CustomDrawer: TPanel;
  LblPathPrompt: TLabel;
  EditPath: TNewEdit;
  BtnBrowse: TNewButton;
  ChkDesktopIcon: TNewCheckBox;
  ChkAutoStart: TNewCheckBox;

  // 安装中 & 完成状态面板
  ProgressStatusLabel: TLabel;
  FinishHeroLabel: TLabel;

// 将 R, G, B 分量转换为 Windows TColor ($00BBGGRR)
function RGB(R, G, B: Byte): TColor;
begin
  Result := R or (G shl 8) or (B shl 16);
end;

// 探测系统当前深色/浅色模式
function DetectIsDarkMode: Boolean;
var
  AppsUseLightTheme: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'AppsUseLightTheme', AppsUseLightTheme) then
    Result := (AppsUseLightTheme = 0);
end;

// 探测是否已存在历史安装版本，并获取历史安装目录（全量覆盖 64 位与 32 位视图）
function DetectPreviousInstallation(var OutPath: String): Boolean;
var
  RegKey: String;
begin
  Result := False;
  OutPath := '';
  RegKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';

  // 1. 优先探测 64 位 HKLM 视图（因为程序以 x64 模式安装）
  if RegQueryStringValue(HKLM64, RegKey, 'InstallLocation', OutPath) or
     RegQueryStringValue(HKLM64, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 2. 探测 32 位 HKLM 视图
  else if RegQueryStringValue(HKLM, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKLM, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 3. 探测 64 位 HKCU 视图
  else if RegQueryStringValue(HKCU64, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKCU64, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 4. 探测 32 位 HKCU 视图
  else if RegQueryStringValue(HKCU, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKCU, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 5. 兜底判定：只要 Uninstall 键存在，即视为已安装
  else if RegKeyExists(HKLM64, RegKey) or RegKeyExists(HKLM, RegKey) or
          RegKeyExists(HKCU64, RegKey) or RegKeyExists(HKCU, RegKey) then
  begin
    Result := True;
    if not (RegQueryStringValue(HKLM64, RegKey, 'UninstallString', OutPath) or
            RegQueryStringValue(HKLM, RegKey, 'UninstallString', OutPath) or
            RegQueryStringValue(HKCU64, RegKey, 'UninstallString', OutPath) or
            RegQueryStringValue(HKCU, RegKey, 'UninstallString', OutPath)) then
      OutPath := '';
    if Length(OutPath) > 0 then
    begin
      OutPath := RemoveQuotes(OutPath);
      OutPath := ExtractFilePath(OutPath);
    end;
  end;

  // 清除尾部反斜杠，保证路径纯净
  if Result and (Length(OutPath) > 0) then
  begin
    if (OutPath[Length(OutPath)] = '\') or (OutPath[Length(OutPath)] = '/') then
      Delete(OutPath, Length(OutPath), 1);
  end;
end;

// 动态 Check 评估函数：桌面快捷方式
function ShouldCreateDesktopIcon: Boolean;
begin
  Result := ChkDesktopIcon.Checked;
end;

// 动态 Check 评估函数：开机自启动计划任务
function ShouldAutoStart: Boolean;
begin
  Result := ChkAutoStart.Checked;
end;

// 初始化颜色设计令牌（严格对齐 Windows 11 Inno Setup 7 真实窗口背景）
procedure InitializeDesignTokens;
begin
  IsDark := DetectIsDarkMode;
  IsUpdateMode := DetectPreviousInstallation(InstalledAppPath);
  IsCustomExpanded := False;

  if IsDark then
  begin
    // Windows 11 Inno Setup 7 真实深色背景 (#2B2B2B)
    ColorBg := RGB(43, 43, 43);
    ColorTextPrimary := RGB(255, 255, 255);  // #FFFFFF
    ColorTextSecondary := RGB(190, 190, 195);// #BEBEC3
    ColorTextMuted := RGB(140, 140, 145);    // #8C8C91
    // 输入框底衬
    ColorInputBg := RGB(32, 32, 32);
    ColorInputBorder := RGB(60, 60, 65);
  end
  else
  begin
    // Windows 11 浅色模式纯白背景
    ColorBg := RGB(255, 255, 255);
    ColorTextPrimary := RGB(10, 10, 10);     // #0A0A0A
    ColorTextSecondary := RGB(70, 70, 70);   // #464646
    ColorTextMuted := RGB(130, 130, 130);    // #828282
    // 输入框底衬
    ColorInputBg := RGB(246, 246, 248);
    ColorInputBorder := RGB(220, 220, 225);
  end;
end;

// 切换自定义选项展开/折叠（使用统一规范的对称 Unicode 指示箭头 ▾ 与 ▴，紧凑自适应高度）
procedure CustomToggleClick(Sender: TObject);
begin
  IsCustomExpanded := not IsCustomExpanded;
  CustomDrawer.Visible := IsCustomExpanded;

  if IsCustomExpanded then
  begin
    CustomToggleLabel.Caption := '收起安装选项 ▴';
    WizardForm.ClientHeight := ScaleY(368);
  end
  else
  begin
    CustomToggleLabel.Caption := '自定义安装选项 ▾';
    WizardForm.ClientHeight := ScaleY(295);
  end;
end;

// 浏览选择安装目录
procedure BrowseFolderClick(Sender: TObject);
var
  ChosenDir: String;
begin
  ChosenDir := EditPath.Text;
  if BrowseForFolder('请选择 SlimClip 的安装目录：', ChosenDir, True) then
  begin
    EditPath.Text := ChosenDir;
  end;
end;

// 安全关闭正在运行的 SlimClip 实例
procedure TerminateRunningSlimClip;
var
  H: HWND;
  I: Integer;
begin
  H := FindWindow('Avalonia-Window-1', 'SlimClip');
  if H = 0 then
    H := FindWindow('', 'SlimClip');

  if H <> 0 then
  begin
    SendMessage(H, WM_CLOSE, 0, 0);
    // 短暂等待优雅退出
    for I := 1 to 12 do
    begin
      Sleep(60);
      if FindWindow('Avalonia-Window-1', 'SlimClip') = 0 then
        Break;
    end;
  end;
end;

// 点击【立即安装 / 立即更新】触发流程
procedure StartInstallClick(Sender: TObject);
begin
  // 1. 同步目标路径到 Inno Setup 内部状态
  if Length(Trim(EditPath.Text)) > 0 then
    WizardForm.DirEdit.Text := Trim(EditPath.Text);

  // 2. 安全退出已运行实例
  TerminateRunningSlimClip;

  // 3. 模拟推进到准备就绪/安装页面
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

// 安装完成后点击【立即体验】
procedure FinishLaunchClick(Sender: TObject);
var
  I: Integer;
  AppPath: String;
  ResultCode: Integer;
begin
  if WizardForm.RunList.Items.Count > 0 then
  begin
    for I := 0 to WizardForm.RunList.Items.Count - 1 do
      WizardForm.RunList.Checked[I] := True;
    WizardForm.NextButton.OnClick(WizardForm.NextButton);
  end
  else
  begin
    // 兜底防御：若 RunList 未就绪，直接拉起应用并关闭安装向导
    AppPath := ExpandConstant('{app}\{#MyAppExeName}');
    if FileExists(AppPath) then
      Exec(AppPath, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    WizardForm.Close;
  end;
end;

// ==============================================================================
// 界面初始化向导 (InitializeWizard)
// ==============================================================================
procedure InitializeWizard;
var
  TmpIconPath: String;
begin
  InitializeDesignTokens;

  // 1. 窗口几何尺寸与背景色（紧凑卡片形态，无多余留白）
  WizardForm.ClientWidth := ScaleX(500);
  WizardForm.ClientHeight := ScaleY(295);
  WizardForm.Position := poScreenCenter;
  WizardForm.Color := ColorBg;

  // 2. 隐藏 Inno Setup 经典组件
  WizardForm.OuterNotebook.Visible := False;
  WizardForm.InnerNotebook.Visible := False;
  WizardForm.Bevel.Visible := False;
  WizardForm.BackButton.Visible := False;
  WizardForm.NextButton.Visible := False;
  WizardForm.CancelButton.Visible := False;

  // 3. 创建全铺满主卡片容器 (ParentBackground 开启实色渲染)
  MainCard := TPanel.Create(WizardForm);
  MainCard.Parent := WizardForm;
  MainCard.Align := alClient;
  MainCard.BevelOuter := bvNone;
  MainCard.BevelInner := bvNone;
  MainCard.ParentBackground := False;
  MainCard.Color := ColorBg;

  // 4. Hero 品牌展示区域 — Logo 图标
  if IsDark then
  begin
    ExtractTemporaryFile('app_logo_dark.bmp');
    TmpIconPath := ExpandConstant('{tmp}\app_logo_dark.bmp');
  end
  else
  begin
    ExtractTemporaryFile('app_logo_light.bmp');
    TmpIconPath := ExpandConstant('{tmp}\app_logo_light.bmp');
  end;

  LogoImg := TBitmapImage.Create(WizardForm);
  LogoImg.Parent := MainCard;
  LogoImg.Width := ScaleX(60);
  LogoImg.Height := ScaleY(60);
  LogoImg.Left := (ScaleX(500) - LogoImg.Width) / 2;
  LogoImg.Top := ScaleY(20);
  LogoImg.Stretch := True;
  if FileExists(TmpIconPath) then
    LogoImg.Bitmap.LoadFromFile(TmpIconPath);

  // 软件名称 Title (显式高度 30px，粗体居中)
  LblTitle := TLabel.Create(WizardForm);
  LblTitle.Parent := MainCard;
  LblTitle.AutoSize := False;
  LblTitle.Left := 0;
  LblTitle.Top := LogoImg.Top + LogoImg.Height + ScaleY(8);
  LblTitle.Width := ScaleX(500);
  LblTitle.Height := ScaleY(30);
  LblTitle.Alignment := taCenter;
  LblTitle.Caption := 'SlimClip';
  LblTitle.Font.Name := 'Segoe UI';
  LblTitle.Font.Size := 18;
  LblTitle.Font.Style := [fsBold];
  LblTitle.Font.Color := ColorTextPrimary;

  // 产品标语 Slogan (显式高度 20px)
  LblSlogan := TLabel.Create(WizardForm);
  LblSlogan.Parent := MainCard;
  LblSlogan.AutoSize := False;
  LblSlogan.Left := 0;
  LblSlogan.Top := LblTitle.Top + LblTitle.Height + ScaleY(2);
  LblSlogan.Width := ScaleX(500);
  LblSlogan.Height := ScaleY(20);
  LblSlogan.Alignment := taCenter;
  LblSlogan.Caption := '轻量 · 纯粹 · 跨平台的现代化剪贴板管理工具';
  LblSlogan.Font.Name := 'Segoe UI';
  LblSlogan.Font.Size := 9;
  LblSlogan.Font.Color := ColorTextSecondary;

  // 版本与状态标签 (显式高度 18px)
  LblVersion := TLabel.Create(WizardForm);
  LblVersion.Parent := MainCard;
  LblVersion.AutoSize := False;
  LblVersion.Left := 0;
  LblVersion.Top := LblSlogan.Top + LblSlogan.Height + ScaleY(2);
  LblVersion.Width := ScaleX(500);
  LblVersion.Height := ScaleY(18);
  LblVersion.Alignment := taCenter;
  if IsUpdateMode then
    LblVersion.Caption := '检测到旧版本，点击即可一键无损升级至 v{#MyAppVersion}'
  else
    LblVersion.Caption := '版本: v{#MyAppVersion} · Windows x64';
  LblVersion.Font.Name := 'Segoe UI';
  LblVersion.Font.Size := 8;
  LblVersion.Font.Color := ColorTextMuted;

  // 5. 核心交互触点：实体大号圆角安装按钮
  BtnInstall := TNewButton.Create(WizardForm);
  BtnInstall.Parent := MainCard;
  BtnInstall.Width := ScaleX(210);
  BtnInstall.Height := ScaleY(40);
  BtnInstall.Left := (ScaleX(500) - BtnInstall.Width) / 2;
  BtnInstall.Top := LblVersion.Top + LblVersion.Height + ScaleY(18);
  BtnInstall.Font.Name := 'Segoe UI';
  BtnInstall.Font.Size := 11;
  BtnInstall.Font.Style := [fsBold];
  if IsUpdateMode then
    BtnInstall.Caption := '立即更新'
  else
    BtnInstall.Caption := '立即安装';
  BtnInstall.Cursor := crHand;
  BtnInstall.OnClick := @StartInstallClick;

  // 6. 居中对称的【自定义安装选项 ▾】辅助触发链接
  CustomToggleLabel := TLabel.Create(WizardForm);
  CustomToggleLabel.Parent := MainCard;
  CustomToggleLabel.AutoSize := False;
  CustomToggleLabel.Left := 0;
  CustomToggleLabel.Top := BtnInstall.Top + BtnInstall.Height + ScaleY(10);
  CustomToggleLabel.Width := ScaleX(500);
  CustomToggleLabel.Height := ScaleY(20);
  CustomToggleLabel.Alignment := taCenter;
  CustomToggleLabel.Caption := '自定义安装选项 ▾';
  CustomToggleLabel.Font.Name := 'Segoe UI';
  CustomToggleLabel.Font.Size := 9;
  CustomToggleLabel.Font.Color := ColorTextMuted;
  CustomToggleLabel.Cursor := crHand;
  CustomToggleLabel.OnClick := @CustomToggleClick;

  // 7. 折叠抽屉自定义面板 (CustomDrawer，紧凑高内聚)
  CustomDrawer := TPanel.Create(WizardForm);
  CustomDrawer.Parent := MainCard;
  CustomDrawer.Left := ScaleX(36);
  CustomDrawer.Top := CustomToggleLabel.Top + CustomToggleLabel.Height + ScaleY(6);
  CustomDrawer.Width := ScaleX(500) - ScaleX(72);
  CustomDrawer.Height := ScaleY(68);
  CustomDrawer.BevelOuter := bvNone;
  CustomDrawer.BevelInner := bvNone;
  CustomDrawer.ParentBackground := False;
  CustomDrawer.Color := ColorBg;
  CustomDrawer.Visible := False;

  // 路径输入框提示标签
  LblPathPrompt := TLabel.Create(WizardForm);
  LblPathPrompt.Parent := CustomDrawer;
  LblPathPrompt.Left := 0;
  LblPathPrompt.Top := ScaleY(4);
  LblPathPrompt.Caption := '安装路径:';
  LblPathPrompt.Font.Name := 'Segoe UI';
  LblPathPrompt.Font.Size := 9;
  LblPathPrompt.Font.Color := ColorTextSecondary;

  // 路径输入框
  EditPath := TNewEdit.Create(WizardForm);
  EditPath.Parent := CustomDrawer;
  EditPath.Left := ScaleX(60);
  EditPath.Top := 0;
  EditPath.Width := CustomDrawer.Width - ScaleX(60) - ScaleX(80);
  EditPath.Height := ScaleY(24);
  if IsUpdateMode and (Length(InstalledAppPath) > 0) then
  begin
    EditPath.Text := InstalledAppPath;
    WizardForm.DirEdit.Text := InstalledAppPath;
  end
  else
    EditPath.Text := WizardForm.DirEdit.Text;
  EditPath.Font.Name := 'Segoe UI';
  EditPath.Font.Size := 9;
  EditPath.Color := ColorInputBg;
  EditPath.Font.Color := ColorTextPrimary;

  BtnBrowse := TNewButton.Create(WizardForm);
  BtnBrowse.Parent := CustomDrawer;
  BtnBrowse.Left := EditPath.Left + EditPath.Width + ScaleX(8);
  BtnBrowse.Top := EditPath.Top;
  BtnBrowse.Width := ScaleX(72);
  BtnBrowse.Height := EditPath.Height;
  BtnBrowse.Caption := '浏览...';
  BtnBrowse.Font.Name := 'Segoe UI';
  BtnBrowse.Font.Size := 9;
  BtnBrowse.OnClick := @BrowseFolderClick;

  // 选项复选框
  ChkDesktopIcon := TNewCheckBox.Create(WizardForm);
  ChkDesktopIcon.Parent := CustomDrawer;
  ChkDesktopIcon.Left := ScaleX(60);
  ChkDesktopIcon.Top := EditPath.Top + EditPath.Height + ScaleY(10);
  ChkDesktopIcon.Width := ScaleX(150);
  ChkDesktopIcon.Caption := '创建桌面快捷方式';
  ChkDesktopIcon.Checked := True;
  ChkDesktopIcon.Font.Name := 'Segoe UI';
  ChkDesktopIcon.Font.Size := 9;
  ChkDesktopIcon.Font.Color := ColorTextSecondary;

  ChkAutoStart := TNewCheckBox.Create(WizardForm);
  ChkAutoStart.Parent := CustomDrawer;
  ChkAutoStart.Left := ChkDesktopIcon.Left + ChkDesktopIcon.Width + ScaleX(12);
  ChkAutoStart.Top := ChkDesktopIcon.Top;
  ChkAutoStart.Width := ScaleX(190);
  ChkAutoStart.Caption := '开机自启动 (免 UAC 计划任务)';
  ChkAutoStart.Checked := True;
  ChkAutoStart.Font.Name := 'Segoe UI';
  ChkAutoStart.Font.Size := 9;
  ChkAutoStart.Font.Color := ColorTextSecondary;

  // 8. 安装中状态与进度条 (初始隐藏)
  ProgressStatusLabel := TLabel.Create(WizardForm);
  ProgressStatusLabel.Parent := MainCard;
  ProgressStatusLabel.AutoSize := False;
  ProgressStatusLabel.Left := 0;
  ProgressStatusLabel.Top := BtnInstall.Top + ScaleY(2);
  ProgressStatusLabel.Width := ScaleX(500);
  ProgressStatusLabel.Height := ScaleY(22);
  ProgressStatusLabel.Alignment := taCenter;
  ProgressStatusLabel.Caption := '正在解压并配置组件，请稍候...';
  ProgressStatusLabel.Font.Name := 'Segoe UI';
  ProgressStatusLabel.Font.Size := 9;
  ProgressStatusLabel.Font.Color := ColorTextSecondary;
  ProgressStatusLabel.Visible := False;

  // 挂载 Inno 原生进度条
  WizardForm.ProgressGauge.Parent := MainCard;
  WizardForm.ProgressGauge.Left := ScaleX(50);
  WizardForm.ProgressGauge.Top := ProgressStatusLabel.Top + ProgressStatusLabel.Height + ScaleY(8);
  WizardForm.ProgressGauge.Width := ScaleX(400);
  WizardForm.ProgressGauge.Height := ScaleY(10);
  WizardForm.ProgressGauge.Visible := False;

  // 9. 安装完成状态 (初始隐藏)
  FinishHeroLabel := TLabel.Create(WizardForm);
  FinishHeroLabel.Parent := MainCard;
  FinishHeroLabel.AutoSize := False;
  FinishHeroLabel.Left := 0;
  FinishHeroLabel.Top := LblVersion.Top + LblVersion.Height + ScaleY(14);
  FinishHeroLabel.Width := ScaleX(500);
  FinishHeroLabel.Height := ScaleY(26);
  FinishHeroLabel.Alignment := taCenter;
  FinishHeroLabel.Font.Name := 'Segoe UI';
  FinishHeroLabel.Font.Size := 14;
  FinishHeroLabel.Font.Style := [fsBold];
  FinishHeroLabel.Font.Color := ColorTextPrimary;
  FinishHeroLabel.Visible := False;
end;

// ==============================================================================
// 页面切换驱动生命周期 (CurPageChanged)
// ==============================================================================
procedure CurPageChanged(CurPageID: Integer);
begin
  // 无论切换至哪个原生向导页面，均隐藏底层传统页并置顶 Fluent 主卡片
  WizardForm.OuterNotebook.Visible := False;
  WizardForm.InnerNotebook.Visible := False;
  WizardForm.BackButton.Visible := False;
  WizardForm.NextButton.Visible := False;
  WizardForm.CancelButton.Visible := False;
  MainCard.BringToFront;

  if CurPageID = wpInstalling then
  begin
    // 进入安装中态：若之前展开了自定义抽屉则平滑收拢并恢复紧凑高度
    if IsCustomExpanded then
    begin
      IsCustomExpanded := False;
      CustomDrawer.Visible := False;
      WizardForm.ClientHeight := ScaleY(295);
    end;
    CustomToggleLabel.Visible := False;
    BtnInstall.Visible := False;

    // 状态文案：居中并紧凑对齐
    ProgressStatusLabel.Top := LblVersion.Top + LblVersion.Height + ScaleY(18);
    ProgressStatusLabel.Width := MainCard.Width;
    ProgressStatusLabel.Left := 0;
    ProgressStatusLabel.Visible := True;
    if IsUpdateMode then
      ProgressStatusLabel.Caption := '正在更新 SlimClip 并配置系统集成...'
    else
      ProgressStatusLabel.Caption := '正在安装 SlimClip 并配置系统集成...';

    // 强制将原生进度条绑定并锁定在 MainCard 绝对居中位置，杜绝向导原生换页引擎的布局重置导致的右侧溢出
    WizardForm.ProgressGauge.Parent := MainCard;
    WizardForm.ProgressGauge.Width := ScaleX(400);
    WizardForm.ProgressGauge.Left := (MainCard.Width - WizardForm.ProgressGauge.Width) / 2;
    WizardForm.ProgressGauge.Top := ProgressStatusLabel.Top + ProgressStatusLabel.Height + ScaleY(12);
    WizardForm.ProgressGauge.Height := ScaleY(10);
    WizardForm.ProgressGauge.Visible := True;
  end
  else if CurPageID = wpFinished then
  begin
    // 安装完成态：隐藏进度条与状态文案
    ProgressStatusLabel.Visible := False;
    WizardForm.ProgressGauge.Visible := False;

    // 展示 Fluent 成功态主标题（深浅自适应 Fluent Success 重音色）
    FinishHeroLabel.Top := LblVersion.Top + LblVersion.Height + ScaleY(16);
    FinishHeroLabel.Width := MainCard.Width;
    FinishHeroLabel.Left := 0;
    FinishHeroLabel.Visible := True;
    if IsDark then
      FinishHeroLabel.Font.Color := RGB(108, 203, 95)
    else
      FinishHeroLabel.Font.Color := RGB(16, 124, 65);

    if IsUpdateMode then
      FinishHeroLabel.Caption := '✓ SlimClip 更新成功'
    else
      FinishHeroLabel.Caption := '✓ SlimClip 安装成功';

    // 实体圆角主按钮变身为【立即体验】高光圆角大按钮
    BtnInstall.Caption := '立即体验';
    BtnInstall.OnClick := @FinishLaunchClick;
    BtnInstall.Width := ScaleX(210);
    BtnInstall.Height := ScaleY(40);
    BtnInstall.Left := (MainCard.Width - BtnInstall.Width) / 2;
    BtnInstall.Top := FinishHeroLabel.Top + FinishHeroLabel.Height + ScaleY(16);
    BtnInstall.Visible := True;
  end;
end;

// 准备安装拦截处理
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  TerminateRunningSlimClip;
end;
