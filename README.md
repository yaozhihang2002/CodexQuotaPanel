# CodexQuotaPanel

CodexQuotaPanel 是一个面向 Windows 10 / Windows 11 x64 的本地 Codex 额度悬浮球。

> 当前版本：**v0.1.1 Pre-release**。这是公开测试版，欢迎通过 GitHub 反馈实际使用中的兼容性和界面问题。

## 主要功能

- 五小时与一周额度双环，可自由选择窗口、角色与颜色。
- 点击悬浮球展开额度详情，支持置顶、透明度、鼠标穿透、自由拖动和尺寸调节。
- 依据近期消耗速度变化的三种火焰效果。
- 深色、浅色、跟随系统主题，以及中文/English 界面。
- 额度提醒、免打扰、24 小时本地趋势、动态托盘额度图标。
- 安装前选择语言，默认简体中文；支持自定义安装目录和可选桌面快捷方式。

## 下载

**[下载 v0.1.1 Windows 安装包](https://github.com/yaozhihang2002/CodexQuotaPanel/releases/download/v0.1.1/CodexQuotaPanel-0.1.1-Setup.exe)** · [查看全部发布文件](https://github.com/yaozhihang2002/CodexQuotaPanel/releases/tag/v0.1.1)

Release 同时提供中文原生 MSI、便携版 ZIP、源码 ZIP 与 SHA-256 校验文件。

## 项目结构

- `work/CodexQuotaPanel`：WinForms 主程序。
- `work/CodexQuotaPanel.Tests`：逻辑检查、布局截图与动画时序检查。
- `work/Installer`：Visual Studio Installer Projects 安装项目。
- `outputs`：本地发布产物，不纳入 Git。

## 构建

```powershell
dotnet build work\CodexQuotaPanel.Tests\CodexQuotaPanel.Tests.csproj -c Release
```

## 基础检查

```powershell
dotnet run --project work\CodexQuotaPanel.Tests\CodexQuotaPanel.Tests.csproj -c Release --no-build
```

发布包为自包含单文件。程序不读取 `auth.json`，不保存对话正文或 token，也不上传额度数据。

## 联系

- GitHub 项目：[yaozhihang2002/CodexQuotaPanel](https://github.com/yaozhihang2002/CodexQuotaPanel)
- Email：[zhyao@mail.ustc.edu.cn](mailto:zhyao@mail.ustc.edu.cn)

## 开源许可证

本项目采用 [MIT License](LICENSE)，允许个人或商业使用、修改、分发与再授权。

## 二创与贡献

欢迎 Fork、改造、重新设计界面或制作自己的衍生版本，也欢迎通过 Issue 和 Pull Request 分享改进。发布二创版本时，请保留原始版权声明和 MIT 许可证文本，并清楚标注你的修改内容。
