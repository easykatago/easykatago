<p align="center">
  <img src="Launcher.App/Assets/app-icon-preview.png" alt="EasyKataGo Icon" width="180" />
</p>

<h1 align="center">EasyKataGo</h1>

<p align="center">面向围棋 AI 引擎 KataGo 的轻量级启动器</p>

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

<p align="center"><sub>聚焦围棋 AI 对弈与分析：一键下载引擎和权重，快速管理多套配置档案，开箱即用。</sub></p>
<p align="center"><sub>引擎GUI由 <a href="https://github.com/yzyray/lizzieyzy">LizzieYzy</a> 强力驱动。</sub></p>

## 界面预览
<p align="center">
  <img src="docs/images/gui-home.png" alt="EasyKataGo GUI Home" width="1000" />
</p>

## 快速下载（推荐）
- 最新版本（安装包 + 便携包）：https://github.com/easykatago/easykatago/releases/latest
- 历史版本列表：https://github.com/easykatago/easykatago/releases

## 运行环境要求
- 本项目基于 `.NET 10`（C# / WPF）开发。
- 使用发布页安装包或便携包（win-x64）时：建议 `Windows 10/11 x64`，默认无需额外安装 .NET 运行时（已自包含发布）。
- 源码构建/调试时：需要安装 `.NET SDK 10.x`。

## 特性
- 面向围棋 AI 引擎 KataGo 的安装、配置与启动管理
- 一键初始化配置与默认档案
- 支持 OpenCL / CUDA / TensorRT 后端选择
- 自动下载引擎、网络权重和默认配置
- 一键基准测试与调优
- 支持手动写入引擎线程
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
