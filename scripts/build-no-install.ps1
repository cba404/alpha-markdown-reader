param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$CleanupToolsAfterBuild
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ToolsDir = Join-Path $Root '.build-tools'
$SdkDir = Join-Path $ToolsDir 'dotnet'
$NuGetDir = Join-Path $ToolsDir 'nuget-packages'
$CliHome = Join-Path $ToolsDir 'dotnet-home'
$InstallScript = Join-Path $ToolsDir 'dotnet-install.ps1'
$Dotnet = Join-Path $SdkDir 'dotnet.exe'
$Project = Join-Path $Root 'src\AlphaNative\AlphaNative.csproj'
$Output = Join-Path $Root "dist\$Runtime"
$Exe = Join-Path $Output 'α.exe'

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KB' -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

function Get-FolderSize([string]$Path) {
    if (-not (Test-Path $Path)) { return 0 }
    return (Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
New-Item -ItemType Directory -Path $NuGetDir -Force | Out-Null
New-Item -ItemType Directory -Path $CliHome -Force | Out-Null

# 所有 SDK、CLI 首次运行文件和 NuGet 包均放在工程内部，便于一次性删除。
$env:NUGET_PACKAGES = $NuGetDir
$env:DOTNET_CLI_HOME = $CliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host '预计首次下载约 270–285 MB；构建期间建议至少预留 1.5 GB 空间。' -ForegroundColor DarkCyan

if (-not (Test-Path $Dotnet)) {
    Write-Host '正在下载微软官方 .NET 安装脚本……' -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $InstallScript

    Write-Host '正在把 .NET 8 SDK 下载到项目目录 .build-tools（不写入系统、不需要管理员权限）……' -ForegroundColor Cyan
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $InstallScript `
        -Channel '8.0' `
        -Quality 'GA' `
        -Architecture 'x64' `
        -InstallDir $SdkDir `
        -NoPath

    if ($LASTEXITCODE -ne 0) { throw '.NET SDK 下载或解压失败。' }
}

if (-not (Test-Path $Dotnet)) {
    throw '未找到本地 .NET SDK，下载可能失败。'
}

Write-Host "使用本地 SDK：$(& $Dotnet --version)" -ForegroundColor DarkCyan
Write-Host "当前构建工具占用：$(Format-Size (Get-FolderSize $ToolsDir))" -ForegroundColor DarkCyan

if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

Write-Host '正在还原 NuGet 依赖……' -ForegroundColor Cyan
& $Dotnet restore $Project -r $Runtime
if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }

Write-Host '正在生成 Windows 自包含单文件程序……' -ForegroundColor Cyan
& $Dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Output

if ($LASTEXITCODE -ne 0) { throw '程序生成失败。' }
if (-not (Test-Path $Exe)) { throw "构建结束，但没有找到 $Exe" }

$ExeSize = (Get-Item -LiteralPath $Exe).Length
$ToolsSize = Get-FolderSize $ToolsDir
Write-Host "`nα 已生成：$Exe" -ForegroundColor Green
Write-Host "程序大小：$(Format-Size $ExeSize)" -ForegroundColor Green
Write-Host "临时构建工具占用：$(Format-Size $ToolsSize)" -ForegroundColor DarkCyan
Write-Host 'α.exe 是自包含程序，目标电脑无需安装 .NET。' -ForegroundColor Green

if ($CleanupToolsAfterBuild) {
    Write-Host '`n正在删除 .build-tools 中的 SDK 与 NuGet 缓存……' -ForegroundColor Cyan
    Remove-Item -LiteralPath $ToolsDir -Recurse -Force
    Write-Host '清理完成，只保留源码和 dist 中的 α.exe。' -ForegroundColor Green
} else {
    Write-Host '需要释放空间时，可双击“清理构建工具.bat”删除 .build-tools。' -ForegroundColor Yellow
}
