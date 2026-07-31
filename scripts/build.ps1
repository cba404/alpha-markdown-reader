$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK 未安装。请安装后重新运行本脚本。'
}

dotnet --info
dotnet restore .\Alpha.sln
dotnet build .\Alpha.sln -c Release --no-restore
Write-Host "`n构建完成：src\AlphaNative\bin\Release\net8.0-windows\" -ForegroundColor Green
