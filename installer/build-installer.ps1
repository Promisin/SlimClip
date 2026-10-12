<#
.SYNOPSIS
    SlimClip 现代 Inno Setup 安装包构建脚本
.DESCRIPTION
    遵循 Inno Setup 现代工程规范，自动探测注册表与环境变量发现 ISCC.exe 编译器，
    并编译生成 Windows Fluent 2 现代一键流式安装向导。
.PARAMETER Version
    发布的版本号，默认为 csproj 中的 Version 属性。
.PARAMETER SourceDir
    发布的程序二进制文件暂存目录，默认为 .publish-staging\windows-x64。
.PARAMETER OutputDir
    安装包生成输出目录，默认为 releases\<Version>。
.PARAMETER IsccPath
    可选的 ISCC.exe 绝对路径。若未提供，将按规范依序从注册表与 PATH 自动探测。
#>

[CmdletBinding()]
param(
    [string]$Version,
    [string]$SourceDir,
    [string]$OutputDir,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = Split-Path -Parent $ScriptDir

# 1. 解析目标版本号
if (-not $Version) {
    $csprojPath = Join-Path $ProjectRoot "src\SlimClip.Desktop\SlimClip.Desktop.csproj"
    if (Test-Path $csprojPath) {
        try {
            $Version = (dotnet msbuild $csprojPath -getProperty:Version 2>$null).Trim()
        }
        catch {
            $Version = $null
        }
    }
    if (-not $Version) {
        $Version = "1.0.3"
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   SlimClip 现代 Inno Setup 安装包构建流程 (v$Version)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. 检查暂存目录与待打包文件
if (-not $SourceDir) {
    $SourceDir = Join-Path $ProjectRoot ".publish-staging\windows-x64"
}
$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)

if (-not (Test-Path (Join-Path $SourceDir "SlimClip.exe"))) {
    Write-Host "[提示] 暂存目录中未检出 SlimClip.exe: $SourceDir" -ForegroundColor Yellow
    Write-Host "正在自动执行发布构建 (dotnet publish)..." -ForegroundColor Gray
    
    $publishArgs = @(
        "publish", (Join-Path $ProjectRoot "src\SlimClip.Desktop\SlimClip.Desktop.csproj"),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:PublishReadyToRun=true",
        "-o", $SourceDir
    )
    & dotnet $publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 执行失败，无法继续打包！"
    }
}

# 3. 检查输出目录
if (-not $OutputDir) {
    $OutputDir = Join-Path $ProjectRoot "releases\$Version"
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# 4. 编译器工具链优先探测规约 (遵循 SKILL.md Section 4.C)
$ResolvedIscc = $IsccPath
if (-not $ResolvedIscc) {
    Write-Host "[1/3] 正在从 Windows 注册表探测 Inno Setup 安装实例..." -ForegroundColor Gray
    $regPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($regPath in $regPaths) {
        $items = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "*Inno Setup*" }
        foreach ($item in $items) {
            if ($item.InstallLocation) {
                $candidate = Join-Path $item.InstallLocation "ISCC.exe"
                if (Test-Path $candidate) {
                    $ResolvedIscc = $candidate
                    break
                }
            }
        }
        if ($ResolvedIscc) { break }
    }
}

if (-not $ResolvedIscc) {
    Write-Host "[2/3] 正在从环境变量 PATH 检索 ISCC.exe..." -ForegroundColor Gray
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $ResolvedIscc = $cmd.Source
    }
}

# 交互闭环规约：若仍未探测到，严禁盲目猜测硬编码路径
if (-not $ResolvedIscc -or -not (Test-Path $ResolvedIscc)) {
    Write-Host ""
    Write-Host "[警告] 未检出 Inno Setup 命令行编译器 ISCC.exe！" -ForegroundColor Red
    Write-Host "遵循自动化工程规范，系统严禁使用盲目猜测路径 fallback。" -ForegroundColor Yellow
    Write-Host "请选择以下方案以完成安装包编译：" -ForegroundColor Yellow
    Write-Host "  1. 访问 Inno Setup 官方主页下载并安装最新 Inno Setup 7: https://jrsoftware.org/isdl.php"
    Write-Host "  2. 通过参数 -IsccPath 手动传入您本地已存在的 ISCC.exe 绝对路径"
    Write-Host ""
    
    if ([Environment]::UserInteractive) {
        $manualPath = Read-Host "请输入 ISCC.exe 完整路径 (留空取消)"
        if ($manualPath -and (Test-Path $manualPath)) {
            $ResolvedIscc = $manualPath
        } else {
            throw "未找到有效的 ISCC.exe，构建中断。"
        }
    } else {
        throw "非交互环境下未检出 ISCC.exe，请配置 PATH 环境变量或指定 -IsccPath。"
    }
}

Write-Host "[3/3] 检出 Inno Setup 编译器: $ResolvedIscc" -ForegroundColor Green
$setupIssPath = Join-Path $ScriptDir "setup.iss"
Write-Host "正在启动 Inno Setup 编译任务..." -ForegroundColor Gray

$isccArgs = @(
    "/dMyAppVersion=$Version",
    "/dSourceDir=$SourceDir",
    "/o$OutputDir",
    $setupIssPath
)

& $ResolvedIscc $isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出码: $LASTEXITCODE"
}

$expectedOutput = Join-Path $OutputDir "SlimClip-v$Version-windows-x64-setup.exe"
if (Test-Path $expectedOutput) {
    $fileInfo = Get-Item $expectedOutput
    $sizeMb = [Math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "   Inno Setup 现代流式安装包构建成功！" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  输出路径: $expectedOutput"
    Write-Host "  文件大小: $sizeMb MB"
    Write-Host "  构建时间: $($fileInfo.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Host "[完成] Inno Setup 编译命令已执行完毕。" -ForegroundColor Green
}
