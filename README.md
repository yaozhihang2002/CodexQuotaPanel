# CodexQuotaPanel

<p align="center">
  <strong>让 Codex 额度安静地待在桌面上，需要时再展开。</strong><br>
  五小时与一周额度双环 · 消耗速度动画 · 本地运行 · 自由定制
</p>

<p align="center">
  <a href="https://github.com/yaozhihang2002/CodexQuotaPanel/releases"><img alt="Release" src="https://img.shields.io/badge/release-v0.3.1--pre--release-64e6b3"></a>
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-1674d1">
  <img alt="Languages" src="https://img.shields.io/badge/UI-简体中文%20%7C%20English-4f8cff">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-f0c674"></a>
</p>

<p align="center">
  <img src="docs/images/detail-panel.png" width="368" alt="CodexQuotaPanel 额度详情面板">
</p>

> 当前版本：**v0.3.1 Pre-release**。这是仍在验证兼容性与界面细节的公开测试版，不代表已经达到稳定版标准。遇到问题欢迎通过 [GitHub Issues](https://github.com/yaozhihang2002/CodexQuotaPanel/issues) 反馈。

## 一眼了解

- **桌面双环悬浮球**：同时查看五小时与一周额度；窗口、环角色与颜色均可调整，点击后展开完整详情。
- **三种风格、五档状态**：简约余烬、流体火焰和像素火焰都会随近期消耗从霜晶、冷焰逐步变化到浓烈大火。
- **适配日常桌面**：深色、浅色或跟随系统，简体中文 / English，支持多显示器、不同 DPI 与负坐标屏幕。
- **自由但克制**：尺寸、字体、透明度、置顶、鼠标穿透、位置锁定、边缘吸附和提醒方式均可设置。
- **本地与可恢复**：额度趋势和设置留在本机；异常记录仅用于脱敏诊断，下次启动照常恢复上次保存的界面、位置和设置。

## 界面预览

### 外观与交互集中设置

悬浮球尺寸、设置字体、透明度、双环颜色、火焰样式、置顶和鼠标穿透都可以调整。修改会即时预览，“保存并应用”后设置窗口仍会保持打开，方便继续微调。

<p align="center">
  <img src="docs/images/settings-appearance.png" width="860" alt="CodexQuotaPanel 外观设置中心">
</p>

### 深色、浅色与跟随系统

<p align="center">
  <img src="docs/images/themes-dark-light.png" width="100%" alt="CodexQuotaPanel 深色和浅色主题">
</p>

### 三种火焰风格，五档消耗反馈

低活动时显示安静的霜晶或冷焰；消耗加快后逐步升温，特别高时显示更浓烈的火焰。三种风格共享五档状态，也可完全关闭动画。

<p align="center">
  <img src="docs/images/flame-styles.png" width="694" alt="CodexQuotaPanel 三种火焰五档状态">
</p>

### 托盘图标也能读懂额度

托盘图标外围会跟随额度变化，并区分连接中、正常、紧张和离线状态，不展开面板也能快速判断当前情况。

<p align="center">
  <img src="docs/images/tray-status.png" width="640" alt="CodexQuotaPanel 动态托盘额度图标">
</p>

## v0.3.1 Pre-release 功能

### 额度与显示

- 五小时与一周额度双环，可选择窗口、内外环角色及自定义颜色。
- 点击悬浮球展开详情，支持本地 24 小时趋势、额度提醒、免打扰时段和最早到期重置卡信息。
- 深色、浅色、跟随系统三种主题，以及简体中文 / English 界面。
- 多显示器与 DPI 保护：跨屏拖动时依据目标显示器缩放，并处理负坐标与可见区域边界。
- 悬浮球位置、大小、字体比例、透明度、置顶和交互偏好会在重启后恢复。

### 动画与交互

- 简约余烬、流体火焰、像素火焰三种样式，每种包含霜晶、冷焰、温焰、热焰和烈焰五档反馈。
- 悬浮球与详情面板采用快速收束 / 展开过渡；拖动交由 Windows 原生窗口移动处理，减少重绘与残影。
- 支持鼠标穿透、位置锁定、可选边缘吸附、全局找回快捷键和动态托盘额度图标。

### 恢复、设置与更新

- 非正常退出或电脑重启后不再进入安全模式，始终按上次保存的显示状态、位置和设置启动。
- 托盘右键菜单提供“重启应用”，在界面仍可响应时快速重新加载程序。
- 安装新版本会主动、安全地关闭正在运行的面板，然后再替换程序文件。
- 穿透提示支持“不再提醒”，也可在“交互”设置中随时恢复；该偏好支持设置导入与导出。
- 额度警告支持“本额度周期不再提醒”，当前窗口重置后自动恢复提醒。
- 设置采用原子写入并保留备份；升级会读取旧版设置，继续保留悬浮球位置和已有个性化参数。
- 支持导入、导出可移植设置。导出文件不包含悬浮球位置、历史、账户、路径或额度数据。
- 可手动检查 GitHub Release，也可选择启动后检查；最多每 24 小时访问一次，不会自动下载或运行安装包。

## 下载

请只从项目的 **[GitHub Releases](https://github.com/yaozhihang2002/CodexQuotaPanel/releases)** 页面下载。`v0.3.1` 会标记为 **Pre-release**，适用于 **Windows 10 / Windows 11 x64**。

Release 发布后可按需要选择安装包或便携包，并使用同时提供的 SHA-256 信息校验文件。预发布版仍可能存在特定显卡、DPI 组合或系统环境下的兼容性问题。

> Windows SmartScreen 可能提示“未知发布者”，这是因为当前预发布版尚未购买代码签名证书。请确认文件来自本项目 Releases 页面后再运行。

## 隐私与数据

程序在本机读取 Codex 客户端产生的可用额度事件，不读取 `auth.json`，不保存对话正文或 token，也不上传额度数据、账号或会话内容。额度是否可显示仍取决于当前电脑上 Codex 客户端产生的数据是否可用。

## 从源码构建

需要 Windows x64 与对应的 .NET SDK：

```powershell
dotnet build work\CodexQuotaPanel.Tests\CodexQuotaPanel.Tests.csproj -c Release
dotnet run --project work\CodexQuotaPanel.Tests\CodexQuotaPanel.Tests.csproj -c Release --no-build
```

项目结构：

- `work/CodexQuotaPanel`：WinForms 主程序。
- `work/CodexQuotaPanel.Tests`：逻辑检查、布局截图与动画时序检查。
- `work/Installer`：Windows 安装项目。
- `docs/images`：README 界面预览素材。
- `outputs`：本地发布产物，不纳入 Git。

## 联系与反馈

- GitHub 项目：[yaozhihang2002/CodexQuotaPanel](https://github.com/yaozhihang2002/CodexQuotaPanel)
- 问题反馈：[GitHub Issues](https://github.com/yaozhihang2002/CodexQuotaPanel/issues)
- Email：[zhyao@mail.ustc.edu.cn](mailto:zhyao@mail.ustc.edu.cn)

## 开源许可证与二创

本项目采用 [MIT License](LICENSE)，允许个人或商业使用、修改、分发与再授权。欢迎 Fork、重新设计界面或制作衍生版本；发布二创时请保留原始版权声明和 MIT 许可证文本，并清楚标注修改内容。
