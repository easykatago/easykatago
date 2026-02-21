# EasyKataGo Launcher

轻量的 Windows 启动器，用于快速安装和启动 `KataGo + LizzieYzy` 组合。

## 快速下载（推荐）
- 最新版本（安装包 + 便携包）：https://github.com/easykatago/easykatago/releases/latest
- 历史版本列表：https://github.com/easykatago/easykatago/releases
- 推荐直接下载并安装 `EasyKataGoLauncher-Setup-vX.Y.Z.exe`。

## 特性
- 一键初始化配置与默认档案
- 支持 OpenCL / CUDA / TensorRT 后端选择
- 自动下载引擎、网络权重和默认配置
- 内置日志与诊断导出

## 开发者：本地构建
```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet build Launcher.Core\Launcher.Core.csproj -nologo
dotnet build Launcher.App\Launcher.App.csproj -nologo
```

## 开发者：本地发布（win-x64）
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1 -SelfContained
```

发布产物位于 `dist\win-x64\`，主程序为 `EasyKataGoLauncher.exe`。

## 许可证
MIT（见 `LICENSE`）

## 鸣谢
- KataGo: https://github.com/lightvector/KataGo
- LizzieYzy: https://github.com/yzyray/lizzieyzy
- KataGo Networks: https://katagotraining.org/networks/
