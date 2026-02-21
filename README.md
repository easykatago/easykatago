# EasyKataGo Launcher

轻量的 Windows 启动器，用于快速安装和启动 `KataGo + LizzieYzy` 组合。

## 特性
- 一键初始化配置与默认档案
- 支持 OpenCL / CUDA / TensorRT 后端选择
- 自动下载引擎、网络权重和默认配置
- 内置日志与诊断导出

## 本地构建
```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet build Launcher.Core\Launcher.Core.csproj -nologo
dotnet build Launcher.App\Launcher.App.csproj -nologo
```

## 本地发布（win-x64）
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1 -SelfContained
```

发布产物位于 `dist\win-x64\`，主程序为 `EasyKataGoLauncher.exe`。

## 许可证
MIT（见 `LICENSE`）
