# CodexQuotaPanel

<p align="center">
  <strong>让 Codex 额度安静地待在桌面上，需要时再展开。</strong><br>
  五小时与一周额度双环 · 消耗速度动画 · 本地运行 · 自由定制
</p>

<p align="center">
  <a href="https://github.com/yaozhihang2002/CodexQuotaPanel/releases"><img alt="Release" src="https://img.shields.io/badge/release-v0.6.9--pre--release-64e6b3"></a>
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-1674d1">
  <img alt="macOS" src="https://img.shields.io/badge/macOS-12%2B%20Apple%20Silicon%20%7C%20Intel-111111">
  <img alt="Languages" src="https://img.shields.io/badge/UI-简体中文%20%7C%20English-4f8cff">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-f0c674"></a>
</p>

<p align="center">
  <img src="docs/images/dashboard-current.png" width="450" alt="CodexQuotaPanel 当前额度详情面板，使用示例数据">
</p>

> 当前版本：**v0.6.9 Pre-release**。这是 Windows / macOS 共用核心与界面的跨平台测试版；macOS 包尚未经过 Developer ID 公证与真人 Retina 设备验收。遇到问题欢迎通过 [GitHub Issues](https://github.com/yaozhihang2002/CodexQuotaPanel/issues) 反馈。

各版本的更新内容与验证说明请查看对应的 [GitHub Releases](https://github.com/yaozhihang2002/CodexQuotaPanel/releases)；当前版本详见 [v0.6.9 介绍](https://github.com/yaozhihang2002/CodexQuotaPanel/releases/tag/v0.6.9)。

## 一眼了解

- **桌面双环悬浮球**：同时查看五小时与一周额度；窗口、环角色与颜色均可调整，点击后展开完整详情。
- **三种风格、五档状态**：简约余烬、流体火焰和像素火焰都会随近期消耗从霜晶、冷焰逐步变化到浓烈大火。
- **适配日常桌面**：深色、浅色或跟随系统，简体中文 / English，支持多显示器、不同 DPI 与负坐标屏幕。
- **自由但克制**：尺寸、字体、透明度、置顶、鼠标穿透、位置锁定、边缘吸附和提醒方式均可设置。
- **本地与可恢复**：额度趋势和设置留在本机；异常记录仅用于脱敏诊断，下次启动照常恢复上次保存的界面、位置和设置。
- **每日用量与 API 成本估算**：按当前重置周期汇总每日原始 Token，并按模型与 `Default` / `Fast` 分类，使用有日期标记的 OpenAI 公开 API 价格估算美元成本。

## 界面预览

以下图片由当前界面的自动化渲染测试生成，使用示例额度与用量，并非真实账户截图或实际账单；系统标题栏和托盘外观会因平台而异。

### 外观与交互集中设置

外观页可即时预览悬浮球，并调整尺寸、界面缩放、背景透明度、环颜色与火焰样式。置顶、鼠标穿透和位置锁定等交互选项位于“交互”页；保存后设置窗口仍会保持打开，方便继续微调。

<p align="center">
  <img src="docs/images/settings-appearance-current.png" width="900" alt="CodexQuotaPanel 当前外观设置页，深色主题">
</p>

### 深色、浅色与跟随系统

同一设置页可选择深色、浅色或跟随系统；下方分别展示深色中文和浅色英文界面。

<p align="center">
  <img src="docs/images/settings-dark-current.png" width="49%" alt="CodexQuotaPanel 深色中文设置界面">
  <img src="docs/images/settings-light-current.png" width="49%" alt="CodexQuotaPanel 浅色英文设置界面">
</p>

### 三种火焰风格，五档消耗反馈

低活动时显示安静的霜晶或冷焰；消耗加快后逐步升温，特别高时显示更浓烈的火焰。三种风格共享五档状态，也可完全关闭动画。

<p align="center">
  <img src="docs/images/feedback-current.png" width="760" alt="CodexQuotaPanel 当前三种火焰风格与五档状态示意">
</p>

### 托盘图标也能读懂额度

托盘图标的环长反映剩余额度，颜色反映实时连接、本地回退、连接中或离线等数据状态。它不是风险等级灯；额度是否紧张请查看悬浮球和详情面板。

## 功能

### 额度与显示

- 五小时与一周额度双环，可选择窗口、内外环角色及自定义颜色。
- 点击悬浮球展开详情，支持分窗口、逐分钟原始精度的完整 24 小时趋势；趋势同时显示半透明的均匀使用参考线，鼠标悬停可对照当时的实际额度与均匀规划额度。
- 智能续航估算会计入空闲区间，并融合 90 分钟短期速度、6 小时长期速度与样本置信度；样本不足时保持原有显示，不读取对话内容。
- 本周期每日图按 API 等价美元估算绘制，悬停仍可核对精确输入、缓存输入、缓存写入、输出与推理用量；点击可展开模型和 `Default` / `Fast` 速率明细。日志较晚写入模型或速率时会在会话内安全回填；Auto-review 按当前官方 Codex 费率表对应的 GPT-5.4 API 价格估算，无法识别或缺少公开费率的模型则保留原始 Token 并标记为“未公开计价”，不会被当作免费或实际账单。
- 每根非零每日柱会直接标出紧凑美元值；设置页同时列明费率日期、Token 组成、Fast、Auto-review 与未计价规则，并提供官方价格入口。
- 续航预测同时展示风险结论、预计还可使用多久和当前/安全速度；最早到期重置卡使用独立高亮状态条展示剩余时长、到期时刻和可用数量。
- Token 统计 2.0 同时兼容单次增量与累计记录，识别计数器重置，并对重复快照、归档副本和分叉日志的复制前缀去重；跨重启持久缓存与追加读取减少重复扫描，详情页会显示缓存命中、去重和归因覆盖率。
- 深色、浅色、跟随系统三种主题，以及简体中文 / English 界面。
- 多显示器与 DPI 保护：跨屏拖动时依据目标显示器缩放，处理负坐标与可见区域边界；远程桌面与 150% / 200% 缩放下，悬浮球及设置内预览也会按目标显示器完整绘制。
- 悬浮球位置、大小、字体比例、透明度、置顶和交互偏好会在重启后恢复。

### 动画与交互

- 简约余烬、流体火焰、像素火焰三种样式，每种包含霜晶、冷焰、温焰、热焰和烈焰五档反馈。
- 悬浮球与详情面板采用快速收束 / 展开过渡；拖动交由 Windows 原生窗口移动处理，设置窗口拉伸时只重排当前页面，减少重绘、闪烁与残影。
- 托盘菜单在鼠标穿透开启时于右侧显示 `✓`，关闭时不显示标记；未穿透时悬浮球使用可点击或可拖动光标。默认字号下详情面板无需滚动即可完整展示额度、续航、重置卡、趋势和每日费用，大字体模式则保留自然滚动以避免压缩重叠。
- 支持鼠标穿透、位置锁定、可选边缘吸附、全局找回快捷键和动态托盘额度图标。

### 恢复、设置与更新

- 非正常退出或电脑重启后不再进入安全模式，始终按上次保存的显示状态、位置和设置启动。
- 托盘右键菜单提供“重启应用”，在界面仍可响应时快速重新加载程序。
- 安装确认页点击“安装”后才会关闭运行中的面板；若安装前处于开启状态，安装结束后自动重新启动。
- 穿透提示支持“不再提醒”，也可在“交互”设置中随时恢复；该偏好支持设置导入与导出。
- 额度警告支持“本额度周期不再提醒”，当前窗口重置后自动恢复提醒。
- 设置采用原子写入并保留备份；升级会读取旧版设置，继续保留悬浮球位置和已有个性化参数。
- 支持导入、导出可移植设置。导出文件不包含悬浮球位置、历史、账户、路径或额度数据。
- 可手动检查 GitHub Release，也可选择启动后检查；最多每 24 小时访问一次，不会自动下载或运行安装包。

## 下载

请只从项目的 **[GitHub Releases](https://github.com/yaozhihang2002/CodexQuotaPanel/releases)** 页面下载。`v0.6.9` 标记为 **Pre-release**，支持 **Windows 10/11 x64** 与 **macOS 12+（Apple Silicon / Intel）**。

- Windows：`CodexQuotaPanel-0.6.9-Windows-Setup.exe`；仅在电脑缺少 .NET 10 时从微软官方下载并验证 SHA-512。
- macOS：`CodexQuotaPanel-0.6.9-macOS.dmg`，一个 DMG 自动覆盖 Apple Silicon 与 Intel Mac。

不再发布重复的 MSI、便携包、ZIP 或离线 Setup，减少选择成本。预发布版仍可能存在特定显卡、DPI 或系统环境下的兼容性问题。

> Windows SmartScreen 可能提示“未知发布者”，这是因为当前预发布版尚未购买代码签名证书。请确认文件来自本项目 Releases 页面后再运行。

## 隐私与数据

程序在本机读取 Codex 客户端产生的可用额度与结构化 Token 计数事件，不读取 `auth.json` 或对话正文。为减少重复扫描，只在本机保存版本化的聚合计数缓存，不保存对话文本，也不上传额度数据、账号或会话内容。美元金额只是按界面标注日期的公开 API 价格作出的等价估算，不是订阅账单，也不是官方额度百分比换算。额度是否可显示仍取决于当前电脑上 Codex 客户端产生的数据是否可用。

## 从源码构建

### Windows / macOS

开发机需要 .NET 10 SDK。Windows Setup 使用框架依赖单文件，并在缺少运行时时从微软下载；macOS DMG 使用自包含通用应用：

```powershell
dotnet restore CodexQuotaPanel.VNext.slnx
dotnet build CodexQuotaPanel.VNext.slnx -c Release
dotnet run --project tests/CodexQuota.Domain.Tests -c Release --no-build
dotnet run --project tests/CodexQuota.Application.Tests -c Release --no-build
dotnet run --project tests/CodexQuota.Infrastructure.Tests -c Release --no-build
dotnet run --project tests/CodexQuota.Platform.Tests -c Release --no-build
dotnet run --project tests/CodexQuota.UI.Tests -c Release --no-build
# 以下原生标题栏检查仅在 Windows 运行
dotnet run --project tests/CodexQuota.NativeTheme.Tests -c Release --no-build
```

Windows 本地候选生成唯一的双语联网 Setup：

```powershell
installer/Windows/Build-Release.ps1 -Version 0.6.9 -DotNetPath <dotnet.exe>
```

macOS 在 Apple runner 或 Mac 上生成唯一的通用 DMG：

```powershell
installer/macOS/Build-Package.ps1 -Version 0.6.9 -Runtime osx-universal -DmgOnly
```

项目结构：

- `src/CodexQuota.App`：桌面应用入口与运行协调。
- `src/CodexQuota.UI.Avalonia`：Windows / macOS 共用界面。
- `src/CodexQuota.Domain`、`Application`、`Infrastructure`：统计与业务逻辑。
- `src/CodexQuota.Platform.Windows`、`Platform.macOS`：平台适配。
- `tests`：逻辑、平台与界面渲染检查。
- `installer/Windows`、`installer/macOS`：两平台安装包脚本。
- `docs/images`：主页当前界面示意图。

## 联系与反馈

- GitHub 项目：[yaozhihang2002/CodexQuotaPanel](https://github.com/yaozhihang2002/CodexQuotaPanel)
- 问题反馈：[GitHub Issues](https://github.com/yaozhihang2002/CodexQuotaPanel/issues)
- Email：[zhyao@mail.ustc.edu.cn](mailto:zhyao@mail.ustc.edu.cn)

## 开源许可证与二创

本项目采用 [MIT License](LICENSE)，允许个人或商业使用、修改、分发与再授权。欢迎 Fork、重新设计界面或制作衍生版本；发布二创时请保留原始版权声明和 MIT 许可证文本，并清楚标注修改内容。
