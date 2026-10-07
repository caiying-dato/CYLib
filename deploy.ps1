<#
.SYNOPSIS
    编译 CYLib，打包 .pck，并安装到游戏的 mods 目录。

.DESCRIPTION
    游戏在运行期间会持有 CYLib.dll 的句柄，运行中部署会报 file is locked。
    本脚本先检查并（询问后）关闭游戏。

    .pck 由 BSchneppe.StS2.PckPacker 在 build 阶段生成（纯 JSON 资源不需要 Godot）；
    本地化文本就在 .pck 里，所以改了 localization/**.json 后必须重新 build。

.EXAMPLE
    pwsh -File .\deploy.ps1
    pwsh -File .\deploy.ps1 -Force        # 不询问，直接关游戏
    pwsh -File .\deploy.ps1 -SkipBuild    # 用已有产物，只做安装与断言
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$modName = 'CYLib'

# ── 定位游戏 ──────────────────────────────────────────────────────
$gameCandidates = @(
    'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    'D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    'D:\Steam\steamapps\common\Slay the Spire 2',
    'E:\Steam\steamapps\common\Slay the Spire 2'
)
$gameDir = $gameCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $gameDir) {
    throw "找不到杀戮尖塔 2 的安装目录。请编辑本脚本顶部的 `$gameCandidates。"
}

$modsRoot = Join-Path $gameDir 'mods'
$modDir = Join-Path $modsRoot $modName

Write-Host "游戏目录 : $gameDir" -ForegroundColor Cyan
Write-Host "模组目录 : $modDir" -ForegroundColor Cyan
Write-Host ''

# ── 1. 确认游戏没有占用 DLL ────────────────────────────────────────
$game = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue
if ($game) {
    if (-not $Force) {
        Write-Host "杀戮尖塔 2 正在运行（pid $($game.Id)），它锁住了 $modName.dll。" -ForegroundColor Yellow
        $answer = Read-Host '现在关闭它吗？[y/N]'
        if ($answer -notmatch '^[Yy]') { throw '已中止。请关闭游戏后重新运行。' }
    }
    Write-Host '正在关闭杀戮尖塔 2……'
    $game | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# ── 2. 编译 + 打包 ────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host '执行 dotnet build -c Debug ……'
    & dotnet build (Join-Path $projectDir "$modName.csproj") -c Debug -v m --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败。' }
}

# ── 3. 确认产物确实落到了 mods 目录 ────────────────────────────────
if (-not (Test-Path -LiteralPath $modDir)) {
    throw "编译后仍找不到 $modDir。请检查 ${modName}.csproj 里的 ModsPath / Sts2Path 设置。"
}

Write-Host ''
Write-Host "已安装到 $modDir" -ForegroundColor Green
Get-ChildItem -LiteralPath $modDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

# ── 4. 在工作区留一份可分发副本 ───────────────────────────────────
$distDir = Join-Path $projectDir "dist\$modName"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Get-ChildItem -LiteralPath $modDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $distDir -Force
}
Write-Host "已归档一份可分发副本到 $distDir" -ForegroundColor Green

# ── 5. 断言 .pck 内容（防"资源目录名写错"这类静默失败）─────────────
$pck = Join-Path $distDir "$modName.pck"
if (-not (Test-Path -LiteralPath $pck)) {
    throw "$modName.pck 没有生成 —— 检查 PckPacker 包引用与 <ResourcesDir>（必须是 $modName）。"
}

# .pck 里的路径字符串是明文，直接搜即可。虽然粗，但非常有效。
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($pck))
$expected = @(
    "$modName/localization/eng/settings_ui.json",
    "$modName/localization/eng/main_menu_ui.json",
    "$modName/localization/zhs/settings_ui.json",
    "$modName/localization/zhs/main_menu_ui.json"
)
foreach ($path in $expected) {
    if ($text.Contains($path)) {
        Write-Host "  OK  pck 含 $path" -ForegroundColor Green
    }
    else {
        throw "pck 缺少 $path —— 检查 ${modName}.csproj 的 <ResourcesDir> 是否等于模组 id（$modName）。"
    }
}

Write-Host ''
Write-Host '完成。启动游戏后到 %APPDATA%\SlayTheSpire2\logs\godot.log 搜 [CYLib]。' -ForegroundColor Green
