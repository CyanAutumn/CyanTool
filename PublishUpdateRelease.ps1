





















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
    [string[]]$RuntimeIdentifiersToPackage = @("win-x64", "win-x86"),
    [switch]$PublishGitUpdateRepo,
    [string]$GitUpdateRepoWorkingDirectory = "",
    [string]$GitUpdateBranch = "main",
    [string]$GitUpdatePayloadPath = "files",
    [string]$GitUpdateManifestPath = "version.json",
    [switch]$SkipReleaseAssetUpload,
    [switch]$SkipVersionXmlUpdate,
    [switch]$SkipProjectVersionUpdate,
    [switch]$SkipTrainingBackendBootstrapResources,
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

function Get-LatestGitUpdateManifestVersion {
    param(
        [string]$Repo,
        [string]$Path,
        [string]$Branch
    )

    $existing = Get-RepoFileContent -Repo $Repo -Path $Path -Branch $Branch
    if ($null -eq $existing -or [string]::IsNullOrWhiteSpace($existing.Content)) {
        return $null
    }

    try {
        $manifest = $existing.Content | ConvertFrom-Json
        if ($manifest -and -not [string]::IsNullOrWhiteSpace($manifest.version)) {
            return [string]$manifest.version
        }
    }
    catch {
        return $null
    }

    return $null
}

function Invoke-GitChecked {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )

    & git -C $WorkingDirectory @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "git 命令失败（exit code: $LASTEXITCODE）：git -C `"$WorkingDirectory`" $($Arguments -join ' ')"
    }
}

function Ensure-GitUpdateRepository {
    param(
        [string]$Repo,
        [string]$WorkingDirectory,
        [string]$Branch
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "未检测到 git。请先安装 Git，或不要使用 -PublishGitUpdateRepo。"
    }

    $repoUrl = "https://github.com/$Repo.git"
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        $parent = Split-Path -Parent $WorkingDirectory
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }

        Write-Host "克隆更新仓库: $repoUrl -> $WorkingDirectory"
        & git clone --branch $Branch $repoUrl $WorkingDirectory | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "克隆更新仓库失败（git exit code: $LASTEXITCODE）"
        }
        return
    }

    if (-not (Test-Path -LiteralPath (Join-Path $WorkingDirectory ".git") -PathType Container)) {
        throw "GitUpdateRepoWorkingDirectory 不是 git 仓库：$WorkingDirectory"
    }

    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("fetch", "origin", $Branch)
    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("checkout", $Branch)
    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("pull", "--ff-only", "origin", $Branch)
}

function Get-PublishRuntimeDirectories {
    param(
        [string]$BuildDir,
        [string[]]$RuntimeIdentifiers
    )

    $runtimesPath = Join-Path $BuildDir "runtimes"
    if (-not (Test-Path -LiteralPath $runtimesPath -PathType Container)) {
        return @()
    }

    $normalizedRuntimeIdentifiers = @()
    if ($null -ne $RuntimeIdentifiers) {
        foreach ($rid in $RuntimeIdentifiers) {
            if (-not [string]::IsNullOrWhiteSpace($rid)) {
                $normalizedRuntimeIdentifiers += $rid.Trim()
            }
        }
    }

    if ($normalizedRuntimeIdentifiers.Count -eq 0 -or $normalizedRuntimeIdentifiers -contains "*") {
        return @(Get-ChildItem -LiteralPath $runtimesPath -Force -Directory)
    }

    $items = @()
    foreach ($rid in @($normalizedRuntimeIdentifiers | Select-Object -Unique)) {
        $runtimePath = Join-Path $runtimesPath $rid
        if (Test-Path -LiteralPath $runtimePath -PathType Container) {
            $items += Get-Item -LiteralPath $runtimePath
        }
        else {
            Write-Warning "指定 runtime 目录不存在，已跳过：runtimes\$rid"
        }
    }

    return @($items)
}

function Copy-PublishItemsToPayload {
    param(
        [string]$BuildDir,
        [string]$PayloadDirectory,
        [string[]]$RuntimeIdentifiers
    )

    if (Test-Path -LiteralPath $PayloadDirectory) {
        Remove-Item -LiteralPath $PayloadDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $PayloadDirectory | Out-Null

    $files = @(Get-ChildItem -LiteralPath $BuildDir -Force -File | Where-Object { $_.Name -notin @("AGENTS.md", "CLAUDE.md", "memory.md", "memory_index.md", "cookies.txt") -and $_.Name -notlike ".env*" })
    $directories = @(Get-ChildItem -LiteralPath $BuildDir -Force -Directory | Where-Object { $_.Name -notin @("runtimes", ".git", ".codex", ".claude", ".vscode", "memory-bank", "Config", "Configs", "config") })
    $runtimeDirectories = @(Get-PublishRuntimeDirectories -BuildDir $BuildDir -RuntimeIdentifiers $RuntimeIdentifiers)
    if (($files.Count + $directories.Count + $runtimeDirectories.Count) -eq 0) {
        throw "目标目录下没有可发布内容：$BuildDir"
    }

    foreach ($file in $files) {
        Copy-Item -LiteralPath $file.FullName -Destination $PayloadDirectory -Force
    }

    foreach ($directory in $directories) {
        Copy-Item -LiteralPath $directory.FullName -Destination $PayloadDirectory -Recurse -Force
    }

    if ($runtimeDirectories.Count -gt 0) {
        $payloadRuntimesDirectory = Join-Path $PayloadDirectory "runtimes"
        New-Item -ItemType Directory -Force -Path $payloadRuntimesDirectory | Out-Null
        foreach ($runtimeDirectory in $runtimeDirectories) {
            Copy-Item -LiteralPath $runtimeDirectory.FullName -Destination $payloadRuntimesDirectory -Recurse -Force
        }
    }
}

function Get-FileSha256 {
    param([string]$Path)

    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

function ConvertTo-ManifestRelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $relative = [System.IO.Path]::GetRelativePath($Root, $Path)
    return ($relative -replace "\\", "/")
}

function Build-GitUpdateManifest {
    param(
        [string]$Repo,
        [string]$Branch,
        [string]$PayloadPath,
        [string]$PayloadDirectory,
        [string]$Version,
        [string]$ReleaseNotes
    )

    $files = @()
    foreach ($file in Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File | Sort-Object FullName) {
        $files += [pscustomobject]@{
            path = ConvertTo-ManifestRelativePath -Root $PayloadDirectory -Path $file.FullName
            size = $file.Length
            sha256 = Get-FileSha256 -Path $file.FullName
        }
    }

    $normalizedPayloadPath = ($PayloadPath -replace "\\", "/").Trim("/")
    return [pscustomobject]@{
        version = $Version
        mandatory = $false
        releaseNotes = $ReleaseNotes
        repo = $Repo
        branch = $Branch
        payloadPath = $normalizedPayloadPath
        rawBaseUrl = "https://raw.githubusercontent.com/$Repo/$Branch/$normalizedPayloadPath"
        files = $files
    } | ConvertTo-Json -Depth 8
}

function Publish-GitUpdateRepository {
    param(
        [string]$Repo,
        [string]$WorkingDirectory,
        [string]$Branch,
        [string]$PayloadPath,
        [string]$ManifestPath,
        [string]$BuildDir,
        [string[]]$RuntimeIdentifiers,
        [string]$Version,
        [string]$ReleaseNotes
    )

    Ensure-GitUpdateRepository -Repo $Repo -WorkingDirectory $WorkingDirectory -Branch $Branch

    $normalizedPayloadPath = ($PayloadPath -replace "\\", [System.IO.Path]::DirectorySeparatorChar).Trim([System.IO.Path]::DirectorySeparatorChar)
    $payloadDirectory = Join-Path $WorkingDirectory $normalizedPayloadPath
    Copy-PublishItemsToPayload -BuildDir $BuildDir -PayloadDirectory $payloadDirectory -RuntimeIdentifiers $RuntimeIdentifiers

    $manifestFile = Join-Path $WorkingDirectory $ManifestPath
    $manifestDirectory = Split-Path -Parent $manifestFile
    if (-not [string]::IsNullOrWhiteSpace($manifestDirectory)) {
        New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
    }

    $manifestJson = Build-GitUpdateManifest `
        -Repo $Repo `
        -Branch $Branch `
        -PayloadPath $PayloadPath `
        -PayloadDirectory $payloadDirectory `
        -Version $Version `
        -ReleaseNotes $ReleaseNotes

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($manifestFile, $manifestJson + "`n", $utf8NoBom)

    $payloadPathForGit = ($PayloadPath -replace "\\", "/").Trim("/")
    $manifestPathForGit = ($ManifestPath -replace "\\", "/")
    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("add", "--all", "--", $payloadPathForGit, $manifestPathForGit)

    $status = & git -C $WorkingDirectory status --porcelain -- $payloadPathForGit $manifestPathForGit
    if ([string]::IsNullOrWhiteSpace(($status -join "`n"))) {
        Write-Host "Git 更新仓库没有文件变化，跳过 commit/push。"
        return
    }

    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("commit", "-m", "Update CyanTools to $Version")
    Invoke-GitChecked -WorkingDirectory $WorkingDirectory -Arguments @("push", "origin", $Branch)
    Write-Host "已推送 Git 增量更新：$Repo $Branch/$ManifestPath"
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
if ([string]::IsNullOrWhiteSpace($GitUpdateRepoWorkingDirectory)) {
    $GitUpdateRepoWorkingDirectory = Join-Path $OutDir "CyanToolsUpdateRepo"
}
if (-not [System.IO.Path]::IsPathRooted($GitUpdateRepoWorkingDirectory)) {
    $GitUpdateRepoWorkingDirectory = Join-Path $baseDir $GitUpdateRepoWorkingDirectory
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
    if ($PublishGitUpdateRepo -and $SkipReleaseAssetUpload) {
        $latestManifestVersion = Get-LatestGitUpdateManifestVersion -Repo $Repo -Path $GitUpdateManifestPath -Branch $GitUpdateBranch
        if (-not [string]::IsNullOrWhiteSpace($latestManifestVersion)) {
            $Version = Increment-Version -VersionString $latestManifestVersion
        }
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $auto = Derive-NextVersionFromLatestRelease -Repo $Repo -TagPrefix $TagPrefix
        if ($null -eq $auto) {
            throw "无法自动推断下一个版本号（可能是仓库无 releases/version.json 或未登录 gh）。请用 -Version 指定，例如：-Version 1.0.0.4"
        }

        $Version = $auto.Version
    }
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
    if (-not $SkipTrainingBackendBootstrapResources) {
        . (Join-Path $baseDir "scripts\Build-TrainingBackendBootstrap.ps1")
        Build-TrainingBackendBootstrap -ProjectRoot $baseDir | Out-Host
    }
    Invoke-DotNetBuild -ProjectPath $resolvedProjectFile -Configuration "Release"
}

if (-not (Test-Path -LiteralPath $BuildDir -PathType Container)) {
    throw "找不到构建输出目录：$BuildDir"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$releaseNotes = Read-ReleaseNotes -ReleaseNotes $ReleaseNotes -ReleaseNotesFile $ReleaseNotesFile -SkipPrompt:$SkipReleaseNotesPrompt
$hasReleaseNotes = -not [string]::IsNullOrWhiteSpace($releaseNotes)

if ($PublishGitUpdateRepo) {
    Publish-GitUpdateRepository `
        -Repo $Repo `
        -WorkingDirectory $GitUpdateRepoWorkingDirectory `
        -Branch $GitUpdateBranch `
        -PayloadPath $GitUpdatePayloadPath `
        -ManifestPath $GitUpdateManifestPath `
        -BuildDir $BuildDir `
        -RuntimeIdentifiers $RuntimeIdentifiersToPackage `
        -Version $Version `
        -ReleaseNotes $releaseNotes
}

if (-not $SkipReleaseAssetUpload) {
    if (Test-Path -LiteralPath $zipArchivePath) {
        Remove-Item -LiteralPath $zipArchivePath -Force
    }

    Write-Host "打包目录: $BuildDir"
    Write-Host "输出文件: $zipArchivePath"
    Write-Host "打包 runtime: $($RuntimeIdentifiersToPackage -join ', ')"

    $packageStagingDirectory = Join-Path $OutDir "_package_$Version"
    try {
        Copy-PublishItemsToPayload `
            -BuildDir $BuildDir `
            -PayloadDirectory $packageStagingDirectory `
            -RuntimeIdentifiers $RuntimeIdentifiersToPackage

        Push-Location $packageStagingDirectory
        try {
            $items = @(Get-ChildItem -LiteralPath $packageStagingDirectory -Force | ForEach-Object { $_.Name })

            if ($items.Count -eq 0) {
                throw "目标目录下没有可打包内容（顶层文件或匹配的 runtimes 目录）：$BuildDir"
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
    }
    finally {
        if (Test-Path -LiteralPath $packageStagingDirectory) {
            Remove-Item -LiteralPath $packageStagingDirectory -Recurse -Force
        }
    }

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
}
else {
    Write-Host "已跳过 Release zip 上传；旧版 AutoUpdater 的 version.xml 未更新。"
}

Write-Host "完成。"
