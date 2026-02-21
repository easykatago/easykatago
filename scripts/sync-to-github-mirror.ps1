param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$MirrorRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MirrorRoot)) {
    $MirrorRoot = Join-Path $SourceRoot "github\easykatago"
}

if (!(Test-Path $SourceRoot)) {
    throw "Source root not found: $SourceRoot"
}

New-Item -ItemType Directory -Path $MirrorRoot -Force | Out-Null

$sourceDirs = @(
    "Launcher.App",
    "Launcher.Core",
    "Launcher.Core.Tests",
    "Launcher.Resources",
    "scripts"
)

$sourceFiles = @(
    "Launcher.sln",
    "README.md",
    ".gitignore",
    "LICENSE"
)

foreach ($dir in $sourceDirs) {
    $from = Join-Path $SourceRoot $dir
    $to = Join-Path $MirrorRoot $dir
    if (!(Test-Path $from)) {
        throw "Missing source directory: $from"
    }

    robocopy $from $to /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP `
        /XD bin obj dist .vs .idea .git .dotnet .dotnet-home .dotnet_cli .templateengine `
        /XF *.log test-run.log host_trace.txt .tmp_* | Out-Null

    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed for '$dir' with exit code $LASTEXITCODE"
    }
}

foreach ($file in $sourceFiles) {
    $from = Join-Path $SourceRoot $file
    if (Test-Path $from) {
        Copy-Item $from -Destination $MirrorRoot -Force
    }
}

Write-Host "Mirror sync complete:"
Write-Host "  Source: $SourceRoot"
Write-Host "  Mirror: $MirrorRoot"
