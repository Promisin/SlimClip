# 贡献指南 (Contributing to SlimClip)

感谢您对 **SlimClip** 的关注！我们欢迎社区提交 Bug 修复、交互调优、新功能与本地化翻译。为了保证跨平台代码质量与架构一致性，请在贡献前阅读以下指引。

---

## 一、行为准则与哲学

- **业务同构、布局一致、原生质感、操作统一**：Windows 与 macOS 双端保持完全一致的交互心智模型，各端使用原生系统设计语言（Windows Fluent 2 / macOS HIG Liquid Glass）。
- **平台专属逻辑隔离**：严禁在 `SlimClip.Core` 或 `SlimClip.UI` 中直接引用操作系统特有 API。所有平台能力必须通过 `SlimClip.Core` 的接口契约抽象，并在 `SlimClip.Platform.*` 中具体实现。
- **MVVM 纯洁性**：使用 `CommunityToolkit.Mvvm`，ViewModel 绝不引用 View 或 Avalonia 控件类型，优先使用编译绑定 `{CompiledBinding}`。

---

## 二、开发环境准备

- **.NET 10.0 SDK**（`10.0.100` 或更高版本）
- **推荐 IDE**：
  - Windows: Visual Studio 2022+ / JetBrains Rider / VS Code (C# Dev Kit)
  - macOS: JetBrains Rider / VS Code (C# Dev Kit)
- **可选工具**：
  - Windows 安装包构建需要 [Inno Setup 6+](https://jrsoftware.org/isdl.php)

---

## 三、构建与运行

### 1. 还原与编译
```bash
# 还原依赖
dotnet restore SlimClip.slnx

# 编译整个解决方案 (Debug)
dotnet build SlimClip.slnx
```

### 2. 运行单元测试
```bash
dotnet test tests/SlimClip.Tests/SlimClip.Tests.csproj
```

### 3. 本地启动运行
```bash
# 启动桌面端应用程序
dotnet run --project src/SlimClip.Desktop/SlimClip.Desktop.csproj
```

---

## 四、Git 提交信息规范 (Conventional Commits)

每次提交必须遵循 Angular 规范，提交信息使用**专业中文**：

`格式：<type>(<scope>): <subject>`

支持的 `<type>`：
- `feat`: 引入新功能
- `fix`: 修复 Bug
- `docs`: 文档变动
- `style`: 格式化或样式调整（不影响运行逻辑）
- `refactor`: 代码重构
- `perf`: 性能与流畅度优化
- `test`: 增加或修改测试用例
- `chore`: 构建过程或辅助工具变动

示例：
- `feat(ui): 增加深色模式自适应切换`
- `fix(macos): 修复全屏应用下光标定位失效问题`
- `test(core): 补充图片哈希去重单元测试`

---

## 五、Pull Request 流程

1. Fork 本仓库并基于 `main` 分支拉出特性分支（如 `feat-dark-mode`）；
2. 保持代码整洁，确保本地 `dotnet test` 全量通过；
3. 遵循 [DESIGN.md](DESIGN.md) 与 [CONTEXT.md](CONTEXT.md) 的设计与术语约束；
4. 提交 PR 并关联相应 Issue，等待 CI 矩阵（Windows & macOS）自动构建验证。
