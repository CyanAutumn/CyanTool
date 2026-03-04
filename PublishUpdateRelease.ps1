
















[CmdletBinding()]
param(
    [string]$Repo = "CyanAutumn/CyanToolsUpdate",
    [string]$ProjectFile = "CyanTools.csproj",
    [string]$BuildDir = "bin\\Release\\net8.0-windows",
    [string]$OutDir = "dist",
    [string]$TagPrefix = "",
    [string]$Version = "",
    [string]$ReleaseNotes = "",
    [string]$ReleaseNotesFile = "",
    [switch]$SkipReleaseNotesPrompt,
    [string]$SevenZipPath = "",
    [string]$VersionXmlPath = "version.xml",
    [string]$VersionXmlBranch = "main",
    [switch]$SkipVersionXmlUpdate,
    [switch]$SkipProjectVersionUpdate,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {

}

function Get-ScriptBaseDir {
    $dir = $PSScriptRoot
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        return $dir
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Split-Path -Parent $PSCommandPath)
    }

    if ($MyInvocation -and $MyInvocation.MyCommand -and -not [string]::IsNullOrWhiteSpace($MyInvocation.MyCommand.Path)) {
        return (Split-Path -Parent $MyInvocation.MyCommand.Path)
    }

    return (Get-Location).Path
}

function Resolve-SevenZipPath {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        if (Test-Path -LiteralPath $PreferredPath -PathType Leaf) {
            return $PreferredPath
        }
        throw "找不到 7z.exe：$PreferredPath"
    }

    $cmd = Get-Command 7z -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @()
    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles "7-Zip\\7z.exe")
    }
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if ($programFilesX86) {
        $candidates += (Join-Path $programFilesX86 "7-Zip\\7z.exe")
    }

    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p -PathType Leaf)) {
            return $p
        }
    }

    throw "未检测到 7z.exe。请安装 7-Zip 并确保 7z.exe 在 PATH 中，或用 -SevenZipPath 指定路径。"
}

function Get-LatestReleaseTag {
    param([string]$Repo)

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        throw "未检测到 GitHub CLI（gh）。请先安装：https://cli.github.com/ 并执行 `gh auth login`。"
    }

    $json = & gh release list --repo $Repo --limit 1 --json tagName 2>$null
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    $data = $json | ConvertFrom-Json
    if ($null -eq $data -or $data.Count -eq 0) {
        return $null
    }
    return $data[0].tagName
}

function Increment-Version {
    param([string]$VersionString)

    $v = if ($null -eq $VersionString) { "" } else { $VersionString }
    $v = $v.Trim()
    if ([string]::IsNullOrWhiteSpace($v)) {
        throw "版本号为空，无法递增。"
    }

    $parts = $v.Split(".")
    if ($parts.Count -lt 3) {
        throw "版本号格式不正确：$v（至少应为 x.y.z 或 x.y.z.n）"
    }

    $lastIndex = $parts.Count - 1
    $parts[$lastIndex] = (([int]$parts[$lastIndex]) + 1).ToString()
    return ($parts -join ".")
}

function Update-LocalProjectVersion {
    param(
        [string]$ProjectPath,
        [string]$NewVersion
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        throw "ProjectPath 不能为空。"
    }
    if ([string]::IsNullOrWhiteSpace($NewVersion)) {
        throw "NewVersion 不能为空。"
    }
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "找不到项目文件：$ProjectPath"
    }

    $text = [System.IO.File]::ReadAllText($ProjectPath)
    $updated = $text

    $updated = [regex]::Replace($updated, '(?s)(<Version>).*?(</Version>)', { param($m) $m.Groups[1].Value + $NewVersion + $m.Groups[2].Value })
    $updated = [regex]::Replace($updated, '(?s)(<AssemblyVersion>).*?(</AssemblyVersion>)', { param($m) $m.Groups[1].Value + $NewVersion + $m.Groups[2].Value })
    $updated = [regex]::Replace($updated, '(?s)(<FileVersion>).*?(</FileVersion>)', { param($m) $m.Groups[1].Value + $NewVersion + $m.Groups[2].Value })

    if ($updated -eq $text) {
        Write-Host "项目版本号未变化（可能已是 $NewVersion）：$ProjectPath"
        return
    }

    $bytes = [System.IO.File]::ReadAllBytes($ProjectPath)
    $hasUtf8Bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $encoding = New-Object System.Text.UTF8Encoding($hasUtf8Bom)
    [System.IO.File]::WriteAllText($ProjectPath, $updated, $encoding)

    Write-Host "已更新项目版本号：$ProjectPath -> $NewVersion"
}

function Invoke-DotNetBuild {
    param(
        [string]$ProjectPath,
        [string]$Configuration = "Release"
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "未检测到 dotnet。请先安装 .NET SDK，或使用 -SkipBuild 跳过构建。"
    }

    Write-Host "开始构建：dotnet build -c $Configuration $ProjectPath"
    $prevErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & dotnet build $ProjectPath -c $Configuration | Out-Host
    }
    finally {
        $ErrorActionPreference = $prevErrorActionPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失败（exit code: $LASTEXITCODE）"
    }
}

function Derive-NextVersionFromLatestRelease {
    param([string]$Repo, [string]$TagPrefix)

    $latestTag = Get-LatestReleaseTag -Repo $Repo
    if ([string]::IsNullOrWhiteSpace($latestTag)) {
        return $null
    }

    $m = [regex]::Match($latestTag, "^(?<prefix>[^0-9]*)(?<version>[0-9].*)$")
    if (-not $m.Success) {
        throw "无法从最新 tag 解析版本号：$latestTag"
    }

    $prefix = $m.Groups["prefix"].Value
    $latestVersion = $m.Groups["version"].Value
    $nextVersion = Increment-Version -VersionString $latestVersion

    return [pscustomobject]@{
        Prefix = $prefix
        LatestTag = $latestTag
        Version = $nextVersion
    }
}

function Get-RepoFileContent {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch = "main"
    )

    $apiPath = "/repos/$Repo/contents/${Path}?ref=$Branch"
    $json = & gh api $apiPath --header "Accept: application/vnd.github+json" 2>$null
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    $obj = $json | ConvertFrom-Json
    if ($null -eq $obj -or $obj.type -ne "file") {
        return $null
    }

    $b64 = ($obj.content -replace "\n", "")
    $bytes = [Convert]::FromBase64String($b64)
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)

    return [pscustomobject]@{
        Sha = $obj.sha
        Content = $content
    }
}

function Put-RepoFileContent {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch,
        [string]$Message,
        [string]$Content,
        [string]$Sha
    )

    $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Content))

    $args = @(
        "api",
        "/repos/$Repo/contents/${Path}",
        "--method", "PUT",
        "--header", "Accept: application/vnd.github+json",
        "-f", "message=$Message",
        "-f", "content=$b64",
        "-f", "branch=$Branch"
    )
    if (-not [string]::IsNullOrWhiteSpace($Sha)) {
        $args += @("-f", "sha=$Sha")
    }

    & gh @args | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "更新仓库文件失败：$Repo/$Path（gh exit code: $LASTEXITCODE）"
    }
}

function Build-VersionXml {
    param(
        [string]$Version,
        [string]$Url,
        [string]$Changelog,
        [string]$Mandatory
    )

    $vEsc = [System.Security.SecurityElement]::Escape($Version)
    $uEsc = [System.Security.SecurityElement]::Escape($Url)
    $cEsc = [System.Security.SecurityElement]::Escape($Changelog)
    $mEsc = [System.Security.SecurityElement]::Escape($Mandatory)

    return @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>$vEsc</version>
    <url>$uEsc</url>
    <changelog>$cEsc</changelog>
    <mandatory>$mEsc</mandatory>
</item>
"@
}

function Update-VersionXml {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch,
        [string]$Version,
        [string]$DownloadUrl
    )
    $defaultMandatory = "false"

    $existing = Get-RepoFileContent -Repo $Repo -Path $Path -Branch $Branch

    $mandatory = $defaultMandatory

    if ($null -ne $existing -and -not [string]::IsNullOrWhiteSpace($existing.Content)) {
        try {
            [xml]$xml = $existing.Content
            if ($xml.item.mandatory) { $mandatory = $xml.item.mandatory }
        }
        catch {
            $mandatory = $defaultMandatory
        }
    }

    $newContent = Build-VersionXml -Version $Version -Url $DownloadUrl -Changelog "" -Mandatory $mandatory
    $sha = if ($null -ne $existing) { $existing.Sha } else { "" }

    Put-RepoFileContent -Repo $Repo -Path $Path -Branch $Branch -Message "Update version.xml to $Version" -Content $newContent -Sha $sha
}

function Read-ReleaseNotes {
    param(
        [string]$ReleaseNotes,
        [string]$ReleaseNotesFile,
        [switch]$SkipPrompt
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesFile)) {
        if (-not (Test-Path -LiteralPath $ReleaseNotesFile -PathType Leaf)) {
            throw "找不到 ReleaseNotes 文件：$ReleaseNotesFile"
        }
        return [System.IO.File]::ReadAllText($ReleaseNotesFile)
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        return $ReleaseNotes
    }

    if ($SkipPrompt) {
        return ""
    }

    Write-Host "请输入更新说明（空行结束，直接回车跳过）"
    $lines = @()
    while ($true) {
        $line = Read-Host
        if ([string]::IsNullOrEmpty($line)) {
            break
        }
        $lines += $line
    }

    return ($lines -join "`r`n")
}

$sevenZip = $null
if (-not [string]::IsNullOrWhiteSpace($SevenZipPath)) {
    $sevenZip = Resolve-SevenZipPath -PreferredPath $SevenZipPath
}
else {
    try {
        $sevenZip = Resolve-SevenZipPath -PreferredPath ""
    }
    catch {
        $sevenZip = $null
    }
}

$baseDir = Get-ScriptBaseDir
if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = "bin\\Release\\net8.0-windows"
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = "dist"
}
if (-not [System.IO.Path]::IsPathRooted($BuildDir)) {
    $BuildDir = Join-Path $baseDir $BuildDir
}
if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $baseDir $OutDir
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "未检测到 GitHub CLI（gh）。请先安装：https://cli.github.com/ 并执行 `gh auth login`。"
}

if (-not $PSBoundParameters.ContainsKey("TagPrefix")) {
    try {
        $latestTag = Get-LatestReleaseTag -Repo $Repo
        if (-not [string]::IsNullOrWhiteSpace($latestTag)) {
            $m = [regex]::Match($latestTag, "^(?<prefix>[^0-9]*)(?<version>[0-9].*)$")
            if ($m.Success) {
                $TagPrefix = $m.Groups["prefix"].Value
            }
        }
    }
    catch {

    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $auto = Derive-NextVersionFromLatestRelease -Repo $Repo -TagPrefix $TagPrefix
    if ($null -eq $auto) {
        throw "无法自动推断下一个版本号（可能是仓库无 releases 或未登录 gh）。请用 -Version 指定，例如：-Version 1.0.0.4"
    }

    $Version = $auto.Version
}

$tag = "$TagPrefix$Version"
$zipAssetName = "$Version.zip"
$zipArchivePath = Join-Path $OutDir $zipAssetName

$resolvedProjectFile = $ProjectFile
if ([string]::IsNullOrWhiteSpace($resolvedProjectFile)) {
    $resolvedProjectFile = "CyanTools.csproj"
}
if (-not [System.IO.Path]::IsPathRooted($resolvedProjectFile)) {
    $resolvedProjectFile = Join-Path $baseDir $resolvedProjectFile
}

if (-not $SkipProjectVersionUpdate) {
    Update-LocalProjectVersion -ProjectPath $resolvedProjectFile -NewVersion $Version
}
if (-not $SkipBuild) {
    Invoke-DotNetBuild -ProjectPath $resolvedProjectFile -Configuration "Release"
}

if (-not (Test-Path -LiteralPath $BuildDir -PathType Container)) {
    throw "找不到构建输出目录：$BuildDir"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (Test-Path -LiteralPath $zipArchivePath) {
    Remove-Item -LiteralPath $zipArchivePath -Force
}

Write-Host "打包目录: $BuildDir"
Write-Host "输出文件: $zipArchivePath"

Push-Location $BuildDir
try {
    $items = @(Get-ChildItem -Force | Select-Object -ExpandProperty Name)

    if ($items.Count -eq 0) {
        throw "目标目录下没有可打包内容：$BuildDir"
    }

    if ($null -ne $sevenZip) {
        & $sevenZip a -tzip -mx=9 $zipArchivePath @items | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "打包 zip 失败（7z exit code: $LASTEXITCODE）"
        }
    }
    else {
        $compress = Get-Command Compress-Archive -ErrorAction SilentlyContinue
        if (-not $compress) {
            throw "未检测到 7z.exe 且当前 PowerShell 不支持 Compress-Archive。请安装 7-Zip，或升级 PowerShell。"
        }
        Compress-Archive -Path $items -DestinationPath $zipArchivePath -Force
    }
}
finally {
    Pop-Location
}

$releaseNotes = Read-ReleaseNotes -ReleaseNotes $ReleaseNotes -ReleaseNotesFile $ReleaseNotesFile -SkipPrompt:$SkipReleaseNotesPrompt
$hasReleaseNotes = -not [string]::IsNullOrWhiteSpace($releaseNotes)

$releaseExists = $false
$prevErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & gh release view $tag --repo $Repo --json tagName 1>$null 2>$null
    $releaseExists = ($LASTEXITCODE -eq 0)
}
finally {
    $ErrorActionPreference = $prevErrorActionPreference
}

if (-not $releaseExists) {
    Write-Host "创建 Release: $Repo $tag"
    $notesToUse = if ($hasReleaseNotes) { $releaseNotes } else { "Auto upload $tag" }
    & gh release create $tag $zipArchivePath --repo $Repo --title $tag --notes $notesToUse | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "创建 Release 失败（gh exit code: $LASTEXITCODE）"
    }
}
else {
    if ($hasReleaseNotes) {
        Write-Host "更新 Release 说明: $Repo $tag"
        & gh release edit $tag --repo $Repo --notes $releaseNotes | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "更新 Release 说明失败（gh exit code: $LASTEXITCODE）"
        }
    }

    Write-Host "更新 Release 资产: $Repo $tag"
    & gh release upload $tag $zipArchivePath --repo $Repo --clobber | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "上传 Release 资产失败（gh exit code: $LASTEXITCODE）"
    }
}

if (-not $SkipVersionXmlUpdate) {
    $downloadUrl = "https://github.com/$Repo/releases/download/$tag/$zipAssetName"
    Write-Host "更新 version.xml: $Repo $VersionXmlBranch/$VersionXmlPath"
    Update-VersionXml -Repo $Repo -Path $VersionXmlPath -Branch $VersionXmlBranch -Version $Version -DownloadUrl $downloadUrl
}

$viewJson = & gh release view $tag --repo $Repo --json url 2>$null
if (-not [string]::IsNullOrWhiteSpace($viewJson)) {
    $url = ($viewJson | ConvertFrom-Json).url
    Write-Host "Release URL: $url"
}

Write-Host "完成。"
