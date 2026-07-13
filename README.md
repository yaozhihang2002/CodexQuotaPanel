# CodexQuotaPanel

CodexQuotaPanel 是一个面向 Windows 10 / Windows 11 x64 的本地 Codex 额度悬浮球。

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
