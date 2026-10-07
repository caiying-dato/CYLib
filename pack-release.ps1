# ============================================================
# CYLib 发版打包 —— 打包 + 逐项核对 + 可选发布到局域网共享架
#
# 用法：
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File pack-release.ps1 -Version 0.1.0
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File pack-release.ps1 -Version 0.1.0 -Publish
#
# 退出码契约：0 = 包做好了且逐项核对过；非 0 = 没做成 / 没验过。
#
# ★ 为什么要有这个脚本（AutoSTS2 0.1.7 的真实事故，2026-09-21）：
#   手工 `Compress-Archive -Path <散着的文件>` 打出来的包**少了 CYLib\ 这一层**，
#   玩家解到 `游戏\mods\` 时文件被直接撒进 mods\，模组管理器根本看不到这个模组——
#   而且不报任何错。所以这里把两件事焊死：
#     ① 包内路径必须逐字等于 `CYLib\<文件名>`（不多不少）；
#     ② 包内 DLL/JSON/PCK 的哈希必须和源目录逐字相同（否则就是把旧构建发出去了）。
#
# ★ 包名规范（2026-10-03 起）：中文名(英文名)-v<版本>-<yyyyMMdd-HHmm>
#   例：全自动爬塔(AutoSTS2)-v0.1.1-20260918-2111.zip / 愈深愈肉(DeeperAndTankier)-v0.1.0-20261002-2128.zip
#   CYLib 目前没有中文名，所以默认只写英文名：CYLib-v0.1.0-<日期>-<时间>.zip；
#   以后定了中文名，加 -CnName '中文名' 即可得到「中文名(CYLib)-v…」。
#
# ★ 「发布」= 只上传到局域网共享架（%USERPROFILE%\.dsh\lan-transfer\outgoing\），
#   **不碰 Steam 创意工坊**（只有用户明确提到「创意工坊」才走 Steam）。
# ============================================================
[CmdletBinding()]
param(
    # 版本号：与 csproj 的 <Version>、CYLib.json 的 version 一致（脚本会核对）
    [Parameter(Mandatory = $true)][string]$Version,

    # 包名里的中文名（可空；空 = 只用英文名，例如 CYLib-v0.1.0-20261003-0430.zip）
    [string]$CnName = '',

    # 英文名（模组 id）
    [string]$ModName = 'CYLib',

    # 包名里的时间戳（默认现在）
    [string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmm'),

    # 产物目录（默认 <工程根>\release）
    [string]$OutDir = '',

    # 顺手拷到 DSH 局域网共享架（并删掉那里旧的 CYLib 包）
    [switch]$Publish,

    # 只检查不打包
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
trap { Write-Host "`n[失败] $_" -ForegroundColor Red; exit 1 }

$projectDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $projectDir 'release' }

# .pck 是 CYLib 的本地化资源包，必须一起发（has_pck = true）
$files = @("$ModName.dll", "$ModName.json", "$ModName.pck", "$ModName.pdb")
$sourceDir = Join-Path $projectDir "dist\$ModName"

# ───────────────────────── 1. 源目录与版本核对 ─────────────────────────
if (-not (Test-Path -LiteralPath $sourceDir)) {
    throw "找不到 $sourceDir —— 先跑 .\deploy.ps1（它会编译 + 装进 mods + 归档一份可分发副本）"
}
foreach ($name in $files) {
    $p = Join-Path $sourceDir $name
    if (-not (Test-Path -LiteralPath $p)) { throw "找不到 $p（先跑 .\deploy.ps1 生成 .pck 与 .pdb）" }
}

# 1a. csproj 的版本号
$csprojText = [System.IO.File]::ReadAllText((Join-Path $projectDir "$ModName.csproj"), [System.Text.Encoding]::UTF8)
$csprojVersion = [regex]::Match($csprojText, '<Version>([^<]+)</Version>').Groups[1].Value
if ($csprojVersion -ne $Version) {
    throw "$ModName.csproj 的 <Version> 是 $csprojVersion，和要发的 $Version 不一致（先改再构建）"
}

# 1b. 模组清单 json 的版本号（清单里带 v 前缀，比较时去掉）
$jsonText = [System.IO.File]::ReadAllText((Join-Path $sourceDir "$ModName.json"), [System.Text.Encoding]::UTF8)
$jsonVersion = [regex]::Match($jsonText, '"version"\s*:\s*"([^"]+)"').Groups[1].Value
if ($jsonVersion.TrimStart('v') -ne $Version) {
    throw "$ModName.json 的 version 是 $jsonVersion，和要发的 $Version 不一致（两处必须同步升）"
}

# 1c. DLL 的 FileVersion
$dllVersion = (Get-Item (Join-Path $sourceDir "$ModName.dll")).VersionInfo.FileVersion
if ($dllVersion -notlike "$Version*") {
    throw "dist 里的 DLL FileVersion 是 $dllVersion，和要发的 $Version 不一致（先改 csproj 再构建）"
}

Write-Host "== 源目录：$sourceDir" -ForegroundColor Cyan
Write-Host "   csproj $csprojVersion / json $jsonVersion / dll $dllVersion"

# 1d. 和游戏 mods 目录里的那份核对（防止把旧副本发出去）
$gameCandidates = @(
    'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    'D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    'D:\Steam\steamapps\common\Slay the Spire 2',
    'E:\Steam\steamapps\common\Slay the Spire 2'
)
$gameDir = $gameCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($gameDir) {
    $modsDir = Join-Path (Join-Path $gameDir 'mods') $ModName
    if (Test-Path -LiteralPath $modsDir) {
        foreach ($name in $files) {
            $a = (Get-FileHash (Join-Path $sourceDir $name) -Algorithm SHA256).Hash
            $b = (Get-FileHash (Join-Path $modsDir $name) -Algorithm SHA256).Hash
            if ($a -cne $b) {
                throw "dist\$ModName\$name 与游戏 mods\$ModName\$name 不一致（dist 是旧的，先跑 .\deploy.ps1）"
            }
        }
        Write-Host "   dist 与 游戏 mods\$ModName 逐文件哈希一致（$($files.Count) 个）" -ForegroundColor Green
    }
    else {
        Write-Host "   提示：游戏里没有 mods\$ModName，跳过与 mods 的比对" -ForegroundColor Yellow
    }
}
else {
    Write-Host "   提示：没找到游戏安装目录，跳过与 mods 的比对" -ForegroundColor Yellow
}

# ───────────────────────── 2. 先在临时目录摆成 CYLib\ 再压 ─────────────────────────
$stage = Join-Path $env:TEMP "cylib-pkg-$Stamp"
$stageMod = Join-Path $stage $ModName
if (Test-Path -LiteralPath $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stageMod -Force | Out-Null
foreach ($name in $files) { Copy-Item (Join-Path $sourceDir $name) -Destination $stageMod -Force }

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$zipName = if ([string]::IsNullOrWhiteSpace($CnName)) { "$ModName-v$Version-$Stamp.zip" }
           else { "$CnName($ModName)-v$Version-$Stamp.zip" }
$zip = Join-Path $OutDir $zipName

if ($DryRun) {
    Write-Host "== -DryRun：会打成 $zip（包内应是 $ModName\<四个文件>），什么都没写" -ForegroundColor Yellow
    exit 0
}

if (Test-Path -LiteralPath $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stageMod -DestinationPath $zip -Force

# ───────────────────────── 3. 逐项核对（包内路径 + 哈希） ─────────────────────────
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $archive.Entries | ForEach-Object { $_.FullName }
    $want = $files | ForEach-Object { "$ModName\$_" }

    # ★ 路径必须逐字相等（不多不少）：根目录散文件、多一层、少一个都拒绝
    $sortedGot = ($names | Sort-Object) -join '|'
    $sortedWant = ($want | Sort-Object) -join '|'
    if ($sortedGot -cne $sortedWant) {
        throw "包内路径不对。应当是「$sortedWant」，实际是「$sortedGot」——" +
              "多半是压缩时把文件散着传给了 Compress-Archive（解到 mods\ 里根本认不出模组）"
    }

    # ★ 包内 DLL/JSON/PCK 的哈希要和源目录逐字相同（别把旧构建发出去）
    $checked = 0
    foreach ($name in @("$ModName.dll", "$ModName.json", "$ModName.pck")) {
        $entry = $archive.Entries | Where-Object { $_.FullName -ceq "$ModName\$name" }
        $tmpFile = Join-Path $env:TEMP "cylib-pkg-check-$name"
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $tmpFile, $true)

        $inZip = (Get-FileHash $tmpFile -Algorithm SHA256).Hash
        $onDisk = (Get-FileHash (Join-Path $sourceDir $name) -Algorithm SHA256).Hash
        if ($inZip -cne $onDisk) { throw "包内 $name 和源目录的不一致：包内 $inZip / 磁盘 $onDisk" }
        Remove-Item $tmpFile -Force
        $checked++
    }
    if ($checked -ne 3) { throw "只核对了 $checked 个文件，应当核对 3 个——拒绝假通过" }

    Write-Host "[通过] 包内路径逐字核对：$sortedWant" -ForegroundColor Green
    Write-Host "[通过] 包内 DLL/JSON/PCK 与源目录哈希逐字一致（核对 $checked/3）" -ForegroundColor Green
    Write-Host ""
    Write-Host "包        ：$zip"
    Write-Host "包名        ：$zipName"
    Write-Host "大小        ：$((Get-Item $zip).Length) 字节"
    Write-Host "包 SHA256   ：$((Get-FileHash $zip -Algorithm SHA256).Hash)"
    Write-Host "DLL SHA256  ：$((Get-FileHash (Join-Path $sourceDir "$ModName.dll") -Algorithm SHA256).Hash)"
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $stage) { Remove-Item $stage -Recurse -Force }
}

# ───────────────────────── 4. 可选：拷到 DSH 局域网共享架 ─────────────────────────
if ($Publish) {
    $shareDir = Join-Path $env:USERPROFILE '.dsh\lan-transfer\outgoing'
    if (-not (Test-Path -LiteralPath $shareDir)) { throw "找不到局域网共享目录 $shareDir" }

    # 先把新包放过去并核对哈希，确认无误后再删旧包（免得中途失败把架上清空了）
    Copy-Item $zip -Destination $shareDir -Force
    $shared = Join-Path $shareDir $zipName
    $sharedHash = (Get-FileHash $shared -Algorithm SHA256).Hash
    if ($sharedHash -cne (Get-FileHash $zip -Algorithm SHA256).Hash) {
        throw "拷到共享架之后哈希变了（$sharedHash）——别让别的设备抓到坏包"
    }

    Get-ChildItem $shareDir -File | Where-Object {
        ($_.Name -like "*$ModName*" -or (-not [string]::IsNullOrWhiteSpace($CnName) -and $_.Name -like "*$CnName*")) -and
        $_.Name -ne $zipName
    } | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "   删掉共享架上的旧包：$($_.Name)"
    }

    Write-Host "[通过] 已放到局域网共享架：$shared（哈希一致）" -ForegroundColor Green
    Write-Host "       下载页：http://127.0.0.1:3080/dsh-lan-xfer/ui" -ForegroundColor Green
}

exit 0
