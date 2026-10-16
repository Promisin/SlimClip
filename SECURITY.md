# SlimClip 安全政策 (Security Policy)

## 1. 软件性质与威胁模型

**SlimClip** 是一款**纯本地单机运行**的桌面剪贴板管理工具：
- 无任何云端服务依赖，亦不建立远程网络连接或接收外部网络请求；
- 不收集、不上传任何用户剪贴板数据与个人信息；
- 数据库与图片文件全部保存在本地磁盘（Windows 为程序根目录便携存储，macOS 为 `~/Library/Application Support/SlimClip/`）。

尽管 SlimClip 是离线单机应用，我们仍然高度重视任何可能威胁用户本地计算环境的安全问题，重点关注以下风险场景：
1. **剪贴板格式解析鲁棒性**：恶意构造的富文本（HTML/RTF）、畸形图片字节流或超长文件名可能引发的拒绝服务（DoS）或本地异常崩溃；
2. **文件越权与路径穿越**：缓存图片落盘、外部文件拖拽导出等场景下是否存在未受控的本地文件写入隐患；
3. **本地敏感数据泄漏**：密码管理器敏感凭据防泄漏机制或未受控的明文导出风险。

## 2. 报告安全漏洞 (Reporting a Vulnerability)

如果您发现了 SlimClip 的安全隐患或漏洞，**请切勿通过公开的 GitHub Issue 进行披露**。

请优先通过 GitHub 提供的安全通报渠道提交私密报告：
👉 **[提交私密安全报告 (GitHub Private Vulnerability Reporting)](https://github.com/Promisin/SlimClip/security/advisories/new)**

我们将收到通报后尽快跟进核实，并在修复完成后统一发布安全补丁更新。感谢您对保护开源软件安全性所作出的贡献！
