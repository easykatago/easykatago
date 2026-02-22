<p align="center">
  <img src="Launcher.App/Assets/app-icon-preview.png" alt="EasyKataGo Icon" width="180" />
</p>

<h1 align="center">EasyKataGo</h1>

<p align="center">KataGo 引擎 + 权重文件一站式管理启动器</p>

<p align="center">
  <a href="https://github.com/easykatago/easykatago/releases/latest">
    <img src="https://img.shields.io/github/v/release/easykatago/easykatago?sort=semver&label=release" alt="Latest Release" />
  </a>
  <img src="https://img.shields.io/badge/platform-Windows%20x64-0078D6" alt="Platform Windows x64" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License" />
  </a>
  <a href="https://github.com/easykatago/easykatago/releases">
    <img src="https://img.shields.io/github/downloads/easykatago/easykatago/total?label=downloads" alt="GitHub Downloads" />
  </a>
</p>

轻量的 Windows 启动器，用于快速安装和启动 `KataGo + LizzieYzy` 组合。

## 快速下载（推荐）
- 最新版本（安装包 + 便携包）：https://github.com/easykatago/easykatago/releases/latest
- 历史版本列表：https://github.com/easykatago/easykatago/releases

## 运行环境要求
- 本项目基于 `.NET 10`（C# / WPF）开发。
- 使用发布页安装包或便携包（win-x64）时：建议 `Windows 10/11 x64`，默认无需额外安装 .NET 运行时（已自包含发布）。
- 源码构建/调试时：需要安装 `.NET SDK 10.x`。

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
