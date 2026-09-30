# Codex Quota Rings

Windows 任务栏上的透明 Codex 额度双圆环。一眼查看 **5 小时**与**每周剩余额度**，无需展开 Codex。

Lightweight, transparent Windows taskbar rings for your remaining Codex 5-hour and weekly quotas. Built with C# and Windows Forms, without Electron or WebView.

![透明双圆环与刷新按钮（示例数据）](docs/preview.png)

![悬停额度卡片（示例数据）](docs/hover-card.png)

> 这是独立的社区工具，与 OpenAI 无隶属关系。界面当前为中文。预览使用模拟数据。

## 功能

- 透明双圆环显示剩余百分比：`5h` 为 5 小时，`7d` 为每周。
- 直接覆盖显示在主任务栏空白区域，不会随托盘图标折叠。
- 悬停圆环自动浮现额度卡片；离开圆环及卡片后自动收起，不抢焦点、不产生额外任务栏图标。
- Exp 下方的小刷新按钮直接查询；默认每 5 分钟自动刷新。
- 横向拖动定位、浅色/深色主题适配、DPI 缩放、可选开机启动。
- 可手动填写 `Exp` 会员到期日，日期保存在本机。
- Codex 桌面窗口关闭后，仍可通过已登录的本机 Codex CLI 查询。

## 下载与运行

从 [Releases](https://github.com/VincentZ-gd/codex-quota-rings/releases) 下载 `CodexQuotaRings-v2.4.0-windows.zip`，完整解压后双击 `CodexQuotaRings.exe`。

直接下载：[Windows 压缩包](https://github.com/VincentZ-gd/codex-quota-rings/releases/download/v2.4.0/CodexQuotaRings-v2.4.0-windows.zip) · [SHA-256 校验值](https://github.com/VincentZ-gd/codex-quota-rings/releases/download/v2.4.0/SHA256SUMS.txt)。Release 下载包由 GitHub Actions 构建。

要求：Windows 10/11、.NET Framework 4.6 或更新版本，以及支持 `app-server` 且已使用 ChatGPT 账号登录的 Codex CLI。本机验证环境为 Windows 11；其他任务栏布局尚未全面验证。

程序依次查找 `CODEX_QUOTA_CODEX_EXE` 环境变量、PATH 中的 `codex.exe`、桌面版安装的 `%LOCALAPPDATA%\OpenAI\Codex\bin` 下的 `codex.exe`。若你的 CLI 安装方式不同，请把 `CODEX_QUOTA_CODEX_EXE` 设置为实际的 **codex.exe** 完整路径，然后重启本工具。仅有 API Key 的登录方式可能没有 ChatGPT 套餐额度。

### 可选：安装及开机启动

先退出已运行的圆环，在解压目录打开 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

安装脚本将程序复制到 `%LOCALAPPDATA%\Programs\CodexQuotaRings`，并为当前用户启用开机启动。也可以绿色运行后右键圆环勾选“开机启动”；此时请保持程序路径不变。

### 操作

| 操作 | 效果 |
| --- | --- |
| 悬停圆环 | 约 0.2 秒后展开详情；移开约 0.28 秒后收起 |
| 移入卡片 / 点击圆环 | 保持显示 / 立即查看详情 |
| 点击 Exp 下方回转箭头 | 立即刷新；查询中显示省略号 |
| 拖动圆环 | 调整横向位置 |
| 右键圆环 | 刷新、设置 Exp、开机启动、恢复位置、退出 |
| 点击卡片 × | 收起详情，重新移入圆环可再次展开 |

## 查询方式与隐私

本工具启动一个短时运行的 `codex app-server --listen stdio://` 进程，只发送初始化和 `account/rateLimits/read` 请求。它不创建模型会话、不发送推理请求，因此查询本身不使用模型推理额度。它仍会发起经过 CLI 认证的网络请求，服务端可能实施请求频率限制。协议说明见 [OpenAI Codex App Server 文档](https://learn.chatgpt.com/docs/app-server)。

认证由官方 CLI 处理。本工具不直接读取或保存账号令牌、浏览器 Cookie 或 API Key，无遥测、无自动上传功能。

本地数据：

- `%LOCALAPPDATA%\CodexQuotaRings\status.json`：最近的额度读数、更新时间、Exp 和显示位置，用于本地诊断；不包含认证凭据。
- `HKCU\Software\CodexQuotaRings`：横向位置和手动填写的 Exp。
- 启用开机启动时，在当前用户的 `Run` 注册表项中保存启动路径。

请勿在公开问题报告中上传自己的认证文件或未经检查的诊断信息。

## Exp 与数据含义

Exp 是**手动记录**的日期，不会自动续期，也不会跟随账号切换。尚未填写时显示“未设置”。在设置窗口取消日期前的勾选即可清除。

目前查阅的官方公开账号及额度接口没有提供会员到期日字段。`resetsAt` 是额度重置时间；额度重置奖励中的 `expiresAt` 是奖励有效期，不能用于推算会员到期日。自动续费的下一次扣款日也不等同于会员终止日期。

圆环显示的是**剩余**额度。灰色表示当前查询失败后保留的上次读数；横线表示暂无对应窗口数据。查询失败的前三次重试间隔为 30 秒，之后恢复 5 分钟。重新启动后的首次查询若失败，则没有跨启动缓存可显示。

## 已知限制

- 使用独立透明置顶窗口覆盖任务栏，未使用 Windows 原生 DeskBand，也不会为任务栏应用图标预留空间；若重叠可手动拖动。
- 主要面向主显示器底部的水平任务栏。多显示器、垂直任务栏及其他 Shell 未完整测试。
- 任务栏持续隐藏超过 2 秒后圆环跟随隐藏。全屏应用中圆环仍可能显示。
- 需要有效的 Codex 登录和网络连接；登录失效时，请在 Codex 中重新登录。
- 实测单次常驻工作集约 49 MB，具体随系统变化；刷新期间 CLI 子进程会额外占用内存。
- 发布文件尚未进行代码签名。

## 从源码构建

使用 Windows 自带 .NET Framework C# 编译器，无需 NuGet：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本构建 GUI 程序，运行额度解析与悬停交互自检，并生成 `dist\CodexQuotaRings-v2.4.0-windows.zip` 及 `SHA256SUMS.txt`。自检使用模拟响应，不需要登录或网络。

`src/CodexQuotaRings.cs` 负责 CLI 查询、解析及菜单；`src/TaskbarView.cs` 负责绘图、任务栏定位和详情窗口。

## 卸载

先右键取消“开机启动”，再选择“退出”，删除解压目录或安装目录即可。若需清除本地数据，可自行删除上述状态目录及 `HKCU\Software\CodexQuotaRings` 设置项。卸载不影响 Codex 登录。

## 反馈与许可证

欢迎通过本仓库 Issues 提交问题，附上 Windows 版本、缩放比例、任务栏布局及复现步骤。项目以 [MIT License](LICENSE) 发布。
