# SlimClip — 极简跨平台剪贴板历史管理工具

[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC_BY--NC--SA_4.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS-blue.svg)](https://github.com/Promisin/SlimClip)
[![Release](https://img.shields.io/github/v/release/Promisin/SlimClip?color=brightgreen)](https://github.com/Promisin/SlimClip/releases)

**SlimClip** 是一款专为 **Windows** 和 **macOS** 打造的现代化、轻量级、原生质感剪贴板管理工具。

核心设计哲学是**“简洁、流畅、纯本地运行”**：在两套操作系统上呈现完全一致的交互心智模型，同时分别深度融入各自系统推荐的原生设计语言（Windows Fluent 2 亚克力与 macOS 液态玻璃质感）。

<div align="center">
  <img src="docs/assets/preview_main.png" alt="SlimClip 剪贴板主面板" width="360" />
  &nbsp;&nbsp;&nbsp;&nbsp;
  <img src="docs/assets/preview_settings.png" alt="SlimClip 设置面板" width="460" />
</div>

---

## 核心特性

- 🎯 **智能光标跟随唤醒**：按下快捷键，面板优先在当前**文字输入光标**位置弹出；无文本光标时贴近鼠标指针，配合智能屏幕边缘防遮挡避让。
- 📋 **全格式原样注入**：完整支持纯文本、富文本（HTML / RTF）、高清图片以及多文件列表复制，选中即可自动粘贴到目标输入框。
- ⚡ **毫秒级极速搜索与分类过滤**：支持通过键盘方向键快速在 全部 / 收藏 / 文本 / 图片 / 文件 分类间极速切换与即时检索。
- 🔒 **纯本地运行，数据完全离线**：**不连网、无云端依赖、不收集任何隐私数据**。历史记录与缓存图片全部安全持久化于本地磁盘。
- 🎨 **双端原生设计语言**：
  - **Windows**：Fluent 2 + Acrylic（桌面亚克力磨砂）+ HarmonyOS Sans 字体排版。
  - **macOS**：Apple HIG + Liquid Glass（原生液态玻璃毛玻璃）+ SF Pro 字体。

---

## 下载与安装

请前往 **[Releases 最新发布页面](https://github.com/Promisin/SlimClip/releases)** 下载对应平台的最新安装包：

### 🪟 Windows (Windows 10 / 11)
- **安装版**：下载 `SlimClip-vX.Y.Z-windows-x64-setup.exe`，一键运行安装（自带自适应 Fluent 向导与快捷方式）；
- **绿色便携版**：下载 `SlimClip-vX.Y.Z-windows-x64.zip`，解压到任意文件夹后直接运行 `SlimClip.exe` 即可（所有数据随目录便携保存）。

### 🍎 macOS (Apple Silicon M系列芯片)
1. 下载 `SlimClip-vX.Y.Z-macos-arm64.dmg`；
2. 双击打开，将 **SlimClip** 拖拽至 **Applications (应用程序)** 文件夹即可。

> [!TIP]
> **macOS 首次打开提示“无法打开 / 已损坏”的处理方式**：  
> 由于开源构建产物未绑定付费 Apple 开发者证书，macOS Gatekeeper 安全机制可能会标记隔离属性。请打开终端（Terminal）执行以下命令即可正常启动：
> ```bash
> xattr -cr /Applications/SlimClip.app
> ```

---

## 常用操作快捷键

| 操作 | Windows 快捷键 | macOS 快捷键 | 说明 |
| :--- | :--- | :--- | :--- |
| **全局呼出 / 隐藏** | `Alt + Shift + V` | `⇧⌘V` | 在当前光标或指针位置悬浮弹出面板 |
| **选择并自动粘贴** | `Enter` 或 双击 | `Enter` 或 双击 | 复制所选项并自动模拟注入粘贴至前台应用 |
| **分类过滤切换** | `←` / `→` | `←` / `→` | 快速切换 全部 / 收藏 / 文本 / 图片 / 文件 过滤 |
| **列表纵向导航** | `↑` / `↓` | `↑` / `↓` | 向上 / 向下切换选中记录 |
| **聚焦搜索输入框** | `Tab` | `Tab` | 从列表卡片一键返回搜索框 |
| **快速固定 / 置顶** | `Ctrl + P` | `⌘P` | 将选中记录加入收藏并在最上方展示 |
| **删除选中记录** | `Ctrl + Delete` | `⌘Delete` | 物理删除当前历史记录项 |
| **关闭 / 隐藏面板** | `Esc` 或 点击外部 | `Esc` 或 点击外部 | 隐藏窗口，不影响后台剪贴板监听 |

*快捷键可在应用内部「设置」面板中自由自定义。*

---

## 开源许可证 (License)

本项目代码基于 **[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)](LICENSE)** 协议开源：
- **允许**：个人非商业用途免费使用、学习、修改与二次分发；
- **禁止**：未经书面授权将本项目的全部或任何衍生版本用于**任何商业盈利目的**；
- **相同方式共享**：任何基于本项目代码的修改与衍生作品，必须继续以相同的 CC BY-NC-SA 4.0 协议开源并保留原作者署名。
