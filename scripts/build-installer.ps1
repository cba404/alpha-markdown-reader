$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

& "$PSScriptRoot\publish-single-exe.ps1" -Runtime win-x64

$Compiler = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Compiler) {
    throw '未找到 Inno Setup 6。请安装 Inno Setup 后重新运行。单文件 exe 已生成在 dist\win-x64。'
}

& $Compiler "$Root\installer\AlphaNative.iss"
Write-Host "`n安装包已生成到 dist\installer。" -ForegroundColor Green
