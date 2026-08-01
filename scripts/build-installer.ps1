$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

& "$PSScriptRoot\publish-single-exe.ps1" -Runtime win-x64
if ($LASTEXITCODE -ne 0) {
    throw "便携版发布失败，退出码：$LASTEXITCODE"
}

# Chocolatey 会为 ISCC.exe 创建命令行 shim。优先从 PATH 查找，
# 再检查 Inno Setup 的标准安装目录和 Chocolatey bin 目录。
$Candidates = [System.Collections.Generic.List[string]]::new()

foreach ($CommandName in @('ISCC.exe', 'ISCC')) {
    $Command = Get-Command $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($Command -and $Command.Source) {
        $Candidates.Add($Command.Source)
    }
}

if (${env:ProgramFiles(x86)}) {
    $Candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'))
}
if ($env:ProgramFiles) {
    $Candidates.Add((Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
}
if ($env:ChocolateyInstall) {
    $Candidates.Add((Join-Path $env:ChocolateyInstall 'bin\ISCC.exe'))
}

$Compiler = $Candidates |
    Where-Object { $_ -and (Test-Path $_) } |
    Select-Object -Unique -First 1

if (-not $Compiler) {
    $CandidateText = ($Candidates | Select-Object -Unique) -join "`n - "
    throw "未找到 Inno Setup 命令行编译器 ISCC.exe。已检查：`n - $CandidateText"
}

Write-Host "使用 Inno Setup 编译器：$Compiler" -ForegroundColor Cyan
& $Compiler "$Root\installer\AlphaNative.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出码：$LASTEXITCODE"
}

$Installer = Get-ChildItem "$Root\dist\installer\*.exe" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $Installer) {
    throw 'Inno Setup 已运行，但 dist\installer 中没有找到安装包。'
}

Write-Host "`n安装包已生成：$($Installer.FullName)" -ForegroundColor Green
