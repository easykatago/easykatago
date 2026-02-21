# EasyKataGo Launcher

## 当前状态
- 已完成 M1 骨架：项目结构、核心接口、中文导航页面、默认资源文件。
- 已提供最小安装动作：在 `安装向导` 页面初始化 `data/profiles.json`、`data/settings.json`、`data/manifest.snapshot.json`。

## 本地构建
```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet build Launcher.Core\Launcher.Core.csproj -nologo
dotnet build Launcher.App\Launcher.App.csproj -nologo
```

## 发布 EXE 启动器（win-x64）
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

默认会输出“框架依赖型” EXE（当前环境可离线发布）。

若本机已提前还原依赖，可加 `-NoRestore`：
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1 -NoRestore
```

若需要“自包含单文件” EXE（体积更大，通常需要联网拉取运行时包）：
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1 -SelfContained
```

发布成功后在 `dist\win-x64\` 下可找到 `EasyKataGoLauncher.exe`。
