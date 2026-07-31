param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Output = Join-Path $Root "dist\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK 未安装。'
}

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

dotnet restore .\src\AlphaNative\AlphaNative.csproj -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore 失败，退出码：$LASTEXITCODE"
}
dotnet publish .\src\AlphaNative\AlphaNative.csproj `
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

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$ExePath = Join-Path $Output 'α.exe'
if (-not (Test-Path $ExePath)) {
    throw "发布命令结束，但没有找到输出文件：$ExePath"
}

Write-Host "`n独立程序已生成：$ExePath" -ForegroundColor Green
