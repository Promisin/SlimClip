; SlimClip — Inno Setup 7 Fluent 2 现代一键流式安装向导脚本
; 遵循项目设计规范与 Inno Setup Skill：
; 1. 单页面流式架构 (Hero Flow)，固定尺寸 (540x400) 零跳动
; 2. Windows 11 DWM 硬件级圆角与发丝边框，移除原生标题栏
; 3. 全状态设计覆盖，初装态与更新态自动分治
; 4. 高级配置面板贴合展开在主按钮上方，就地过渡至安装与完成态
; 5. 彻底跳过隐藏原生向导页，单次点击瞬发安装

#define MyAppId "{{67EF0B38-FFC8-4D35-B1E3-FFEC27AE6503}"
#define MyAppName "SlimClip"
#define MyAppPublisher "Promisin"
#define MyAppURL "https://github.com/Promisin/SlimClip"
#define MyAppExeName "SlimClip.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\.publish-staging\windows-x64"
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

; 权限模型：使用系统管理员权限安装（支持全局提权应用兼容与免 UAC 计划任务自启）
PrivilegesRequired=admin

; 启用 Inno Setup 7 Windows 11 现代深浅自适应引擎，禁止多余向导页
WizardStyle=modern dynamic windows11 hidebevels
UsedUserAreasWarning=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=no

; 静默探测系统语言，杜绝弹出多余语言选择框
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
CloseApplications=force
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; 内置深浅色双模式预合成高清位图资源
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
Filename: "taskkill.exe"; Parameters: "/F /IM ""{#MyAppExeName}"" /T"; Flags: runhidden; RunOnceId: "KillAppTask"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""{#MyAppName}"" /f"; Flags: runhidden; RunOnceId: "DelAutoStartTask"

[Code]
// ==============================================================================
// Win32 / DWM 原生 API 声明
// ==============================================================================
const
  WM_SYSCOMMAND = $0112;
  SC_DRAGMOVE = $F012;
  SC_MINIMIZE = $F020;
  GWL_STYLE = -16;
  WS_CAPTION = $00C00000;
  WS_THICKFRAME = $00040000;
  WS_SYSMENU = $00080000;
  SWP_NOSIZE = $0001;
  SWP_NOMOVE = $0002;
  SWP_NOZORDER = $0004;
  SWP_NOACTIVATE = $0010;
  SWP_FRAMECHANGED = $0020;
  DWMWA_WINDOW_CORNER_PREFERENCE = 33;
  DWMWA_BORDER_COLOR = 34;
  DWMWCP_ROUND = 2;
  PBM_SETSTATE = 1040;
  PBST_NORMAL = 1;

procedure ReleaseCapture;
  external 'ReleaseCapture@user32.dll stdcall';

function SendMessage(hWnd: HWND; Msg: UINT; wParam: LongInt; lParam: LongInt): LongInt;
  external 'SendMessageW@user32.dll stdcall';

function GetWindowLong(hWnd: HWND; nIndex: Integer): LongInt;
  external 'GetWindowLongW@user32.dll stdcall';

function SetWindowLong(hWnd: HWND; nIndex: Integer; dwNewLong: LongInt): LongInt;
  external 'SetWindowLongW@user32.dll stdcall';

function SetWindowPos(hWnd: HWND; hWndInsertAfter: HWND; X: Integer; Y: Integer; cx: Integer; cy: Integer; uFlags: UINT): BOOL;
  external 'SetWindowPos@user32.dll stdcall';

function DwmSetWindowAttribute(hwnd: HWND; attr: DWORD; var attrValue: DWORD; attrSize: DWORD): HRESULT;
  external 'DwmSetWindowAttribute@dwmapi.dll stdcall';

// ==============================================================================
// 全局界面组件与主题状态
// ==============================================================================
var
  IsDark: Boolean;
  IsUpdateMode: Boolean;
  IsCustomExpanded: Boolean;
  InstalledAppPath: String;

  // 颜色设计令牌 (对齐 DESIGN.md 与 Windows 11 Fluent 2 规范)
  ColorBg, ColorTextPrimary, ColorTextSecondary, ColorTextMuted: TColor;
  ColorBorderDwm: DWORD;
  ColorSuccess: TColor;

  // 容器组件与自定义无缝标题栏
  MainCard: TPanel;
  TitleBarPanel: TPanel;
  TitleBarText: TLabel;
  DragLabel: TLabel;
  BtnTitleClose: TLabel;
  BtnTitleMin: TLabel;

  // Hero 品牌展示
  LogoImg: TBitmapImage;
  LblTitle, LblSlogan, LblVersion: TLabel;

  // 核心交互主安装按钮 (采用原生 TNewButton，在 Inno Windows 11 现代引擎下自绘 Fluent 实体大按钮)
  BtnInstall: TNewButton;

  // 已安装路径展示组件 (更新模式)
  LblCurrentPathTitle: TLabel;
  LblCurrentPathValue: TLabel;

  // 自定义展开按钮与抽屉 (全新安装模式)
  CustomToggleLabel: TLabel;
  CustomDrawer: TPanel;
  LblPathPrompt: TLabel;
  EditPath: TNewEdit;
  BtnBrowse: TNewButton;
  ChkDesktop: TNewCheckBox;
  ChkAutoStart: TNewCheckBox;

  // 安装中 & 完成状态面板
  ProgressStatusLabel: TLabel;
  FinishHeroLabel: TLabel;
  FinishSubLabel: TLabel;

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

  // 1. 优先使用 Inno Setup 内部记录的上一次安装路径
  if (WizardForm.PrevAppDir <> '') and DirExists(WizardForm.PrevAppDir) then
  begin
    OutPath := WizardForm.PrevAppDir;
    Result := True;
    Exit;
  end;

  RegKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';

  // 2. 探测 64 位 HKLM 视图
  if RegQueryStringValue(HKLM64, RegKey, 'InstallLocation', OutPath) or
     RegQueryStringValue(HKLM64, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 3. 探测 32 位 HKLM 视图
  else if RegQueryStringValue(HKLM, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKLM, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 4. 探测 64 位 HKCU 视图
  else if RegQueryStringValue(HKCU64, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKCU64, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 5. 探测 32 位 HKCU 视图
  else if RegQueryStringValue(HKCU, RegKey, 'InstallLocation', OutPath) or
          RegQueryStringValue(HKCU, RegKey, 'Inno Setup: App Path', OutPath) then
    Result := True
  // 6. 兜底判定：只要 Uninstall 键存在，即提取其父级路径
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
  if ChkDesktop <> nil then
    Result := ChkDesktop.Checked
  else
    Result := True;
end;

// 动态 Check 评估函数：开机自启动计划任务
function ShouldAutoStart: Boolean;
begin
  if ChkAutoStart <> nil then
    Result := ChkAutoStart.Checked
  else
    Result := True;
end;

// 初始化颜色设计令牌（对齐 DESIGN.md 与 Windows 11 Fluent 2 视觉规范）
procedure InitializeDesignTokens;
begin
  IsDark := DetectIsDarkMode;
  IsUpdateMode := DetectPreviousInstallation(InstalledAppPath);
  IsCustomExpanded := False;

  if IsDark then
  begin
    ColorBg := RGB(24, 24, 26);              // #18181A 墨黑底色
    ColorTextPrimary := RGB(255, 255, 255);  // #FFFFFF 纯白正文
    ColorTextSecondary := RGB(204, 204, 204);// #CCCCCC 次级铅灰
    ColorTextMuted := RGB(140, 140, 145);    // #8C8C91 弱标注
    ColorBorderDwm := $003E3E3E;             // BGR: #3E3E3E 柔和暗色外框
    ColorSuccess := RGB(108, 203, 95);       // Fluent 2 现代明绿
  end
  else
  begin
    ColorBg := RGB(255, 255, 255);           // #FFFFFF 纯白底色
    ColorTextPrimary := RGB(10, 10, 10);     // #0A0A0A 墨黑正文
    ColorTextSecondary := RGB(80, 80, 80);   // #505050 次要文字
    ColorTextMuted := RGB(138, 138, 144);    // #8A8A90 辅助浅灰
    ColorBorderDwm := $00D0D0D0;             // BGR: #D0D0D0 浅灰外框
    ColorSuccess := RGB(16, 124, 65);        // Fluent 2 墨绿
  end;
end;

// 应用 Windows 11 原生圆角、DWM 柔和边框与无边框窗体样式
procedure ApplyRoundedCorners;
var
  CornerPref: DWORD;
  BorderColor: DWORD;
  Style: LongInt;
begin
  // 1. 彻底清除 WS_CAPTION、WS_THICKFRAME 与 WS_SYSMENU，保证窗口客户区与外框绝对吻合
  Style := GetWindowLong(WizardForm.Handle, GWL_STYLE);
  if (Style and WS_CAPTION) <> 0 then Style := Style - WS_CAPTION;
  if (Style and WS_THICKFRAME) <> 0 then Style := Style - WS_THICKFRAME;
  if (Style and WS_SYSMENU) <> 0 then Style := Style - WS_SYSMENU;
  SetWindowLong(WizardForm.Handle, GWL_STYLE, Style);
  SetWindowPos(WizardForm.Handle, 0, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_NOACTIVATE or SWP_FRAMECHANGED);

  // 2. 注入 Windows 11 DWM 硬件抗锯齿圆角与边框色 (GPU 硬件原生光栅化)
  CornerPref := DWMWCP_ROUND;
  DwmSetWindowAttribute(WizardForm.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, CornerPref, 4);
  BorderColor := ColorBorderDwm;
  DwmSetWindowAttribute(WizardForm.Handle, DWMWA_BORDER_COLOR, BorderColor, 4);
end;

// 窗口无边框拖拽处理
procedure WindowMouseDown(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  if Button = mbLeft then
  begin
    ReleaseCapture;
    SendMessage(WizardForm.Handle, WM_SYSCOMMAND, SC_DRAGMOVE, 0);
  end;
end;

// 自定义标题栏关闭按钮
procedure TitleCloseClick(Sender: TObject);
begin
  WizardForm.CancelButton.Visible := True;
  WizardForm.CancelButton.Enabled := True;
  WizardForm.Close;
end;

// 取消与退出拦截钩子
procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  // 在尚未开始安装 (CurPageID < wpInstalling) 或安装完成 (CurPageID = wpFinished) 时，直接静默秒退，无需二次确认弹窗
  if (CurPageID < wpInstalling) or (CurPageID = wpFinished) then
  begin
    Confirm := False;
    Cancel := True;
  end;
end;

// 自定义标题栏最小化按钮
procedure TitleMinClick(Sender: TObject);
begin
  SendMessage(WizardForm.Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
end;

// 切换自定义选项展开/折叠（仅在全新安装模式下生效，窗体尺寸恒定 540x400 零跳动）
procedure CustomToggleClick(Sender: TObject);
begin
  if IsUpdateMode then Exit;

  IsCustomExpanded := not IsCustomExpanded;
  CustomDrawer.Visible := IsCustomExpanded;

  if IsCustomExpanded then
  begin
    CustomToggleLabel.Top := ScaleY(176);
    CustomToggleLabel.Caption := '收起安装选项 ▴';
  end
  else
  begin
    CustomToggleLabel.Top := ScaleY(278);
    CustomToggleLabel.Caption := '自定义安装选项 ▾';
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
    WizardForm.DirEdit.Text := ChosenDir;
  end;
end;

procedure EditPathChange(Sender: TObject);
begin
  WizardForm.DirEdit.Text := EditPath.Text;
end;

// 安全关闭正在运行的 SlimClip 实例
procedure TerminateRunningSlimClip;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(150);
end;

// 跳过隐藏的原生向导中间页，彻底根治多页跳转拦截
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if (PageID = wpSelectTasks) or (PageID = wpReady) or (PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) then
    Result := True
  else
    Result := False;
end;

// 点击【立即安装 / 立即更新】触发流程（单次瞬发推进，杜绝重复点击）
procedure StartInstallClick(Sender: TObject);
begin
  // 1. 同步目标路径到 Inno Setup 内部状态
  if IsUpdateMode and (Length(InstalledAppPath) > 0) then
    WizardForm.DirEdit.Text := InstalledAppPath
  else if (EditPath <> nil) and (Length(Trim(EditPath.Text)) > 0) then
    WizardForm.DirEdit.Text := Trim(EditPath.Text);

  // 2. 安全退出已运行实例
  TerminateRunningSlimClip;

  // 3. 极速推进至 wpInstalling，避免隐藏步骤停滞
  while (WizardForm.CurPageID < wpInstalling) do
  begin
    WizardForm.NextButton.OnClick(WizardForm.NextButton);
  end;
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
    AppPath := ExpandConstant('{app}\{#MyAppExeName}');
    if FileExists(AppPath) then
      Exec(AppPath, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    WizardForm.Close;
  end;
end;

// ==============================================================================
// 界面初始化向导 (InitializeWizard)
// 规范：窗体尺寸始终固定为 540x400，全状态设计覆盖
// ==============================================================================
procedure InitializeWizard;
var
  TmpIconPath: String;
begin
  InitializeDesignTokens;

  // 1. 去掉系统原生标题栏，采用 Windows 11 Fluent 2 原生圆角自定义无边框卡片形态
  WizardForm.BorderStyle := bsNone;
  WizardForm.ClientWidth := ScaleX(540);
  WizardForm.ClientHeight := ScaleY(400);
  WizardForm.Position := poScreenCenter;
  WizardForm.Color := ColorBg;
  WizardForm.Font.Name := 'Segoe UI';
  ApplyRoundedCorners;

  // 2. 隐藏 Inno Setup 经典原生容器与控制按钮
  WizardForm.OuterNotebook.Visible := False;
  WizardForm.InnerNotebook.Visible := False;
  WizardForm.Bevel.Visible := False;
  WizardForm.BackButton.Visible := False;
  WizardForm.NextButton.Visible := False;
  // 保持 CancelButton 处于 Visible 与 Enabled 状态以响应 FormCloseQuery 模态退出，将其移出可视区
  WizardForm.CancelButton.Left := -1000;
  WizardForm.CancelButton.Top := -1000;
  WizardForm.CancelButton.Visible := True;
  WizardForm.CancelButton.Enabled := True;

  // 3. 创建全铺满主卡片容器 (ParentBackground 开启实色渲染)
  MainCard := TPanel.Create(WizardForm);
  MainCard.Parent := WizardForm;
  MainCard.Align := alClient;
  MainCard.BevelOuter := bvNone;
  MainCard.BevelInner := bvNone;
  MainCard.ParentBackground := False;
  MainCard.Color := ColorBg;

  // 3.1 创建自定义顶部无缝标题栏 (36px)
  TitleBarPanel := TPanel.Create(WizardForm);
  TitleBarPanel.Parent := MainCard;
  TitleBarPanel.Left := 0;
  TitleBarPanel.Top := 0;
  TitleBarPanel.Width := ScaleX(540);
  TitleBarPanel.Height := ScaleY(36);
  TitleBarPanel.BevelOuter := bvNone;
  TitleBarPanel.BevelInner := bvNone;
  TitleBarPanel.ParentBackground := False;
  TitleBarPanel.Color := ColorBg;

  // 标题栏大面积拖拽热区
  DragLabel := TLabel.Create(WizardForm);
  DragLabel.Parent := TitleBarPanel;
  DragLabel.AutoSize := False;
  DragLabel.Left := 0;
  DragLabel.Top := 0;
  DragLabel.Width := TitleBarPanel.Width - ScaleX(84);
  DragLabel.Height := TitleBarPanel.Height;
  DragLabel.Caption := '';
  DragLabel.Transparent := True;
  DragLabel.OnMouseDown := @WindowMouseDown;

  TitleBarText := TLabel.Create(WizardForm);
  TitleBarText.Parent := TitleBarPanel;
  TitleBarText.Left := ScaleX(16);
  TitleBarText.Top := ScaleY(9);
  TitleBarText.Caption := 'SlimClip 安装向导';
  TitleBarText.Font.Name := 'Segoe UI';
  TitleBarText.Font.Size := 9;
  TitleBarText.Font.Color := ColorTextSecondary;
  TitleBarText.OnMouseDown := @WindowMouseDown;

  BtnTitleClose := TLabel.Create(WizardForm);
  BtnTitleClose.Parent := TitleBarPanel;
  BtnTitleClose.AutoSize := False;
  BtnTitleClose.Width := ScaleX(34);
  BtnTitleClose.Height := ScaleY(26);
  BtnTitleClose.Left := TitleBarPanel.Width - BtnTitleClose.Width - ScaleX(6);
  BtnTitleClose.Top := ScaleY(5);
  BtnTitleClose.Alignment := taCenter;
  BtnTitleClose.Caption := '✕';
  BtnTitleClose.Font.Name := 'Segoe UI';
  BtnTitleClose.Font.Size := 10;
  BtnTitleClose.Font.Color := ColorTextSecondary;
  BtnTitleClose.OnClick := @TitleCloseClick;
  BtnTitleClose.Cursor := crHand;

  BtnTitleMin := TLabel.Create(WizardForm);
  BtnTitleMin.Parent := TitleBarPanel;
  BtnTitleMin.AutoSize := False;
  BtnTitleMin.Width := ScaleX(34);
  BtnTitleMin.Height := ScaleY(26);
  BtnTitleMin.Left := BtnTitleClose.Left - BtnTitleMin.Width - ScaleX(2);
  BtnTitleMin.Top := ScaleY(5);
  BtnTitleMin.Alignment := taCenter;
  BtnTitleMin.Caption := '—';
  BtnTitleMin.Font.Name := 'Segoe UI';
  BtnTitleMin.Font.Size := 10;
  BtnTitleMin.Font.Color := ColorTextSecondary;
  BtnTitleMin.OnClick := @TitleMinClick;
  BtnTitleMin.Cursor := crHand;

  // 4. Hero 品牌展示区域 — 高清 Logo 图标
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
  LogoImg.Width := ScaleX(56);
  LogoImg.Height := ScaleY(56);
  LogoImg.Left := (ScaleX(540) - LogoImg.Width) / 2;
  LogoImg.Top := ScaleY(44);
  LogoImg.Stretch := True;
  if FileExists(TmpIconPath) then
    LogoImg.Bitmap.LoadFromFile(TmpIconPath);

  // 软件名称 Title (18pt 粗体居中)
  LblTitle := TLabel.Create(WizardForm);
  LblTitle.Parent := MainCard;
  LblTitle.AutoSize := False;
  LblTitle.Left := 0;
  LblTitle.Top := ScaleY(104);
  LblTitle.Width := ScaleX(540);
  LblTitle.Height := ScaleY(28);
  LblTitle.Alignment := taCenter;
  LblTitle.Caption := 'SlimClip';
  LblTitle.Font.Name := 'Segoe UI';
  LblTitle.Font.Size := 18;
  LblTitle.Font.Style := [fsBold];
  LblTitle.Font.Color := ColorTextPrimary;
  LblTitle.OnMouseDown := @WindowMouseDown;

  // 产品标语 Slogan (9pt 居中)
  LblSlogan := TLabel.Create(WizardForm);
  LblSlogan.Parent := MainCard;
  LblSlogan.AutoSize := False;
  LblSlogan.Left := 0;
  LblSlogan.Top := ScaleY(134);
  LblSlogan.Width := ScaleX(540);
  LblSlogan.Height := ScaleY(18);
  LblSlogan.Alignment := taCenter;
  LblSlogan.Caption := '轻量 · 纯粹 · 跨平台的现代化剪贴板管理工具';
  LblSlogan.Font.Name := 'Segoe UI';
  LblSlogan.Font.Size := 9;
  LblSlogan.Font.Color := ColorTextSecondary;
  LblSlogan.OnMouseDown := @WindowMouseDown;

  // 版本与平台标签
  LblVersion := TLabel.Create(WizardForm);
  LblVersion.Parent := MainCard;
  LblVersion.AutoSize := False;
  LblVersion.Left := 0;
  LblVersion.Top := ScaleY(154);
  LblVersion.Width := ScaleX(540);
  LblVersion.Height := ScaleY(18);
  LblVersion.Alignment := taCenter;
  LblVersion.Caption := 'v{#MyAppVersion} · Windows x64';
  LblVersion.Font.Name := 'Segoe UI';
  LblVersion.Font.Size := 8;
  LblVersion.Font.Color := ColorTextMuted;
  LblVersion.OnMouseDown := @WindowMouseDown;

  // 5. 更新模式：已安装路径纯净只读信息行（彻底杜绝假输入框误解与光标闪烁）
  LblCurrentPathTitle := TLabel.Create(WizardForm);
  LblCurrentPathTitle.Parent := MainCard;
  LblCurrentPathTitle.AutoSize := False;
  LblCurrentPathTitle.Left := 0;
  LblCurrentPathTitle.Top := ScaleY(236);
  LblCurrentPathTitle.Width := ScaleX(540);
  LblCurrentPathTitle.Height := ScaleY(18);
  LblCurrentPathTitle.Alignment := taCenter;
  LblCurrentPathTitle.Caption := '将覆盖更新至现有目录：';
  LblCurrentPathTitle.Font.Name := 'Segoe UI';
  LblCurrentPathTitle.Font.Size := 9;
  LblCurrentPathTitle.Font.Color := ColorTextMuted;
  LblCurrentPathTitle.OnMouseDown := @WindowMouseDown;
  LblCurrentPathTitle.Visible := IsUpdateMode;

  LblCurrentPathValue := TLabel.Create(WizardForm);
  LblCurrentPathValue.Parent := MainCard;
  LblCurrentPathValue.AutoSize := False;
  LblCurrentPathValue.Left := ScaleX(40);
  LblCurrentPathValue.Top := ScaleY(258);
  LblCurrentPathValue.Width := ScaleX(460);
  LblCurrentPathValue.Height := ScaleY(22);
  LblCurrentPathValue.Alignment := taCenter;
  LblCurrentPathValue.Caption := InstalledAppPath;
  LblCurrentPathValue.Font.Name := 'Segoe UI';
  LblCurrentPathValue.Font.Size := 10;
  LblCurrentPathValue.Font.Color := ColorTextPrimary;
  LblCurrentPathValue.OnMouseDown := @WindowMouseDown;
  LblCurrentPathValue.Visible := IsUpdateMode;

  // 6. 全新安装模式：高级配置抽屉面板 (折叠时收拢在主按钮正上方)
  CustomDrawer := TPanel.Create(WizardForm);
  CustomDrawer.Parent := MainCard;
  CustomDrawer.Left := ScaleX(40);
  CustomDrawer.Top := ScaleY(202);
  CustomDrawer.Width := ScaleX(460);
  CustomDrawer.Height := ScaleY(100);
  CustomDrawer.BevelOuter := bvNone;
  CustomDrawer.BevelInner := bvNone;
  CustomDrawer.ParentBackground := False;
  CustomDrawer.Color := ColorBg;
  CustomDrawer.Visible := False;

  // 路径输入框提示标签
  LblPathPrompt := TLabel.Create(WizardForm);
  LblPathPrompt.Parent := CustomDrawer;
  LblPathPrompt.Left := 0;
  LblPathPrompt.Top := ScaleY(7);
  LblPathPrompt.Caption := '安装路径:';
  LblPathPrompt.Font.Name := 'Segoe UI';
  LblPathPrompt.Font.Size := 9;
  LblPathPrompt.Font.Color := ColorTextSecondary;

  EditPath := TNewEdit.Create(WizardForm);
  EditPath.Parent := CustomDrawer;
  EditPath.Left := ScaleX(64);
  EditPath.Top := 0;
  EditPath.Width := ScaleX(310);
  EditPath.Height := ScaleY(30);
  EditPath.Font.Name := 'Segoe UI';
  EditPath.Font.Size := 9;
  EditPath.Text := WizardForm.DirEdit.Text;
  EditPath.OnChange := @EditPathChange;

  BtnBrowse := TNewButton.Create(WizardForm);
  BtnBrowse.Parent := CustomDrawer;
  BtnBrowse.Left := ScaleX(382);
  BtnBrowse.Top := 0;
  BtnBrowse.Width := ScaleX(78);
  BtnBrowse.Height := ScaleY(30);
  BtnBrowse.Caption := '浏览...';
  BtnBrowse.Font.Name := 'Segoe UI';
  BtnBrowse.Font.Size := 9;
  BtnBrowse.Cursor := crHand;
  BtnBrowse.OnClick := @BrowseFolderClick;

  ChkDesktop := TNewCheckBox.Create(WizardForm);
  ChkDesktop.Parent := CustomDrawer;
  ChkDesktop.Left := ScaleX(64);
  ChkDesktop.Top := ScaleY(44);
  ChkDesktop.Width := ScaleX(180);
  ChkDesktop.Height := ScaleY(20);
  ChkDesktop.Caption := '创建桌面快捷方式';
  ChkDesktop.Font.Name := 'Segoe UI';
  ChkDesktop.Font.Size := 9;
  ChkDesktop.Checked := True;

  ChkAutoStart := TNewCheckBox.Create(WizardForm);
  ChkAutoStart.Parent := CustomDrawer;
  ChkAutoStart.Left := ScaleX(250);
  ChkAutoStart.Top := ScaleY(44);
  ChkAutoStart.Width := ScaleX(210);
  ChkAutoStart.Height := ScaleY(20);
  ChkAutoStart.Caption := '开机自启动 (免 UAC 计划任务)';
  ChkAutoStart.Font.Name := 'Segoe UI';
  ChkAutoStart.Font.Size := 9;
  ChkAutoStart.Checked := True;

  // 7. 【自定义安装选项 ▾】折叠触发标签（全新安装模式显示，收起态居中紧贴主按钮）
  CustomToggleLabel := TLabel.Create(WizardForm);
  CustomToggleLabel.Parent := MainCard;
  CustomToggleLabel.AutoSize := False;
  CustomToggleLabel.Left := ScaleX(40);
  CustomToggleLabel.Top := ScaleY(278);
  CustomToggleLabel.Width := ScaleX(460);
  CustomToggleLabel.Height := ScaleY(22);
  CustomToggleLabel.Alignment := taCenter;
  CustomToggleLabel.Caption := '自定义安装选项 ▾';
  CustomToggleLabel.Font.Name := 'Segoe UI';
  CustomToggleLabel.Font.Size := 9;
  CustomToggleLabel.Font.Color := ColorTextMuted;
  CustomToggleLabel.Cursor := crHand;
  CustomToggleLabel.OnClick := @CustomToggleClick;
  CustomToggleLabel.Visible := not IsUpdateMode;

  // 8. 核心交互主按钮（宽度对齐 460px 统一视觉网格，原生 Windows 11 Fluent 实体大按钮）
  BtnInstall := TNewButton.Create(WizardForm);
  BtnInstall.Parent := MainCard;
  BtnInstall.Left := ScaleX(40);
  BtnInstall.Top := ScaleY(312);
  BtnInstall.Width := ScaleX(460);
  BtnInstall.Height := ScaleY(44);
  BtnInstall.Font.Name := 'Segoe UI';
  BtnInstall.Font.Size := 11;
  BtnInstall.Font.Style := [fsBold];
  if IsUpdateMode then
    BtnInstall.Caption := '立即更新'
  else
    BtnInstall.Caption := '立即安装';
  BtnInstall.Cursor := crHand;
  BtnInstall.OnClick := @StartInstallClick;

  // 9. 安装中状态与进度条 (初始隐藏)
  ProgressStatusLabel := TLabel.Create(WizardForm);
  ProgressStatusLabel.Parent := MainCard;
  ProgressStatusLabel.AutoSize := False;
  ProgressStatusLabel.Left := 0;
  ProgressStatusLabel.Top := ScaleY(226);
  ProgressStatusLabel.Width := ScaleX(540);
  ProgressStatusLabel.Height := ScaleY(24);
  ProgressStatusLabel.Alignment := taCenter;
  ProgressStatusLabel.Caption := '正在配置组件，请稍候...';
  ProgressStatusLabel.Font.Name := 'Segoe UI';
  ProgressStatusLabel.Font.Size := 10;
  ProgressStatusLabel.Font.Color := ColorTextPrimary;
  ProgressStatusLabel.Visible := False;

  WizardForm.ProgressGauge.Parent := MainCard;
  WizardForm.ProgressGauge.Left := ScaleX(40);
  WizardForm.ProgressGauge.Top := ScaleY(266);
  WizardForm.ProgressGauge.Width := ScaleX(460);
  WizardForm.ProgressGauge.Height := ScaleY(8);
  WizardForm.ProgressGauge.Visible := False;

  // 10. 安装完成状态 (初始隐藏)
  FinishHeroLabel := TLabel.Create(WizardForm);
  FinishHeroLabel.Parent := MainCard;
  FinishHeroLabel.AutoSize := False;
  FinishHeroLabel.Left := 0;
  FinishHeroLabel.Top := ScaleY(198);
  FinishHeroLabel.Width := ScaleX(540);
  FinishHeroLabel.Height := ScaleY(32);
  FinishHeroLabel.Alignment := taCenter;
  FinishHeroLabel.Font.Name := 'Segoe UI';
  FinishHeroLabel.Font.Size := 16;
  FinishHeroLabel.Font.Style := [fsBold];
  FinishHeroLabel.Font.Color := ColorSuccess;
  FinishHeroLabel.Visible := False;

  FinishSubLabel := TLabel.Create(WizardForm);
  FinishSubLabel.Parent := MainCard;
  FinishSubLabel.AutoSize := False;
  FinishSubLabel.Left := 0;
  FinishSubLabel.Top := ScaleY(240);
  FinishSubLabel.Width := ScaleX(540);
  FinishSubLabel.Height := ScaleY(20);
  FinishSubLabel.Alignment := taCenter;
  FinishSubLabel.Caption := '轻量剪贴板已就绪，按快捷键随时唤醒';
  FinishSubLabel.Font.Name := 'Segoe UI';
  FinishSubLabel.Font.Size := 9;
  FinishSubLabel.Font.Color := ColorTextSecondary;
  FinishSubLabel.Visible := False;
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
  WizardForm.CancelButton.Left := -1000;
  WizardForm.CancelButton.Top := -1000;
  WizardForm.CancelButton.Visible := True;
  WizardForm.CancelButton.Enabled := True;
  MainCard.BringToFront;

  if CurPageID = wpInstalling then
  begin
    // 进入安装中态：平滑隐藏交互项，尺寸始终锁定 540x400
    CustomToggleLabel.Visible := False;
    CustomDrawer.Visible := False;
    LblCurrentPathTitle.Visible := False;
    LblCurrentPathValue.Visible := False;
    BtnInstall.Visible := False;

    // 状态文案
    ProgressStatusLabel.Visible := True;
    if IsUpdateMode then
      ProgressStatusLabel.Caption := '正在更新 SlimClip 并配置系统集成...'
    else
      ProgressStatusLabel.Caption := '正在安装 SlimClip 并配置系统集成...';

    // 显示进度条
    WizardForm.ProgressGauge.Visible := True;
    SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETSTATE, PBST_NORMAL, 0);
  end
  else if CurPageID = wpFinished then
  begin
    // 安装完成态：隐藏进度条与状态文案
    ProgressStatusLabel.Visible := False;
    WizardForm.ProgressGauge.Visible := False;

    // 展示 Fluent 成功态主标题
    FinishHeroLabel.Visible := True;
    FinishSubLabel.Visible := True;
    FinishHeroLabel.Font.Color := ColorSuccess;

    if IsUpdateMode then
      FinishHeroLabel.Caption := '✓ SlimClip 更新成功'
    else
      FinishHeroLabel.Caption := '✓ SlimClip 安装成功';

    // 实体圆角主按钮就地变身为【立即体验】大按钮
    BtnInstall.Caption := '立即体验';
    BtnInstall.OnClick := @FinishLaunchClick;
    BtnInstall.Visible := True;
  end;
end;

// 准备安装拦截处理
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  TerminateRunningSlimClip;
end;
