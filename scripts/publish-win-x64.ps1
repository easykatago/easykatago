param(
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = $root
$outDir = Join-Path $root "dist\\win-x64"

Write-Host "Publishing launcher to $outDir ..."
$cmd = @(
  "publish", "$root\\Launcher.App\\Launcher.App.csproj",
  "-c", $Configuration,
  "-p:RestoreIgnoreFailedSources=true",
  "-p:RestoreSources=",
  "/p:DebugType=None",
  "/p:DebugSymbols=false",
  "-o", $outDir
)

if ($SelfContained) {
  $cmd += @(
    "-r", "win-x64",
    "--self-contained", "true",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
  )
}

if ($NoRestore) {
  $cmd += "--no-restore"
}

dotnet @cmd
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed; EXE not generated."
}

$exe = Get-ChildItem $outDir -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) {
  throw "Publish finished but EXE not found: $outDir"
}

Write-Host "Done. EXE path: $exe"
