param(
    [ValidateSet("wsl", "sd-scripts", "anima-fast-lora", "wsl-sd-scripts", "diffusion-pipe")]
    [string]$Backend = "diffusion-pipe",

    [ValidateSet("Check", "InstallWsl", "InstallUbuntu", "Install", "Update", "Reinstall", "Validate", "DeleteUbuntu")]
    [string]$Action = "Check",

    [ValidateSet("default", "tsinghua", "aliyun")]
    [string]$Mirror = "default",

    [int]$LocalProxyPort = 0,

    [string]$ManagedRoot = "",

    [string]$RuntimeToolsRoot = "",

    [string]$OfflineWheelhouse = "",

    [string]$ManagedRootSeed = "",

    [string]$ResultPath = "",

    [switch]$ForceReinstallDistribution
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

if ([string]::IsNullOrWhiteSpace($ManagedRoot)) {
    $ManagedRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "CyanTools\training-backends"
}

function Resolve-DeploymentResourceDirectory {
    param([string]$ExplicitPath, [string]$EnvironmentName, [string]$DirectoryName)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) { $candidates.Add($ExplicitPath) }
    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentName)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) { $candidates.Add($environmentValue) }
    $candidates.Add((Join-Path (Split-Path -Parent $PSScriptRoot) $DirectoryName))
    $candidates.Add((Join-Path (Split-Path -Parent $PSScriptRoot) "resources\$DirectoryName"))
    $candidates.Add((Join-Path (Split-Path -Parent $PSScriptRoot) "dist\$DirectoryName"))
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ""
}

$RuntimeToolsRoot = Resolve-DeploymentResourceDirectory $RuntimeToolsRoot "CYANTOOLS_RUNTIME_TOOLS_DIR" "runtime-tools"
$OfflineWheelhouse = Resolve-DeploymentResourceDirectory $OfflineWheelhouse "CYANTOOLS_OFFLINE_WHEELHOUSE_DIR" "offline-wheelhouse"
$ManagedRootSeed = Resolve-DeploymentResourceDirectory $ManagedRootSeed "CYANTOOLS_MANAGED_ROOT_SEED_DIR" "managed-root-seed"

function Copy-MissingSeedEntries {
    param([string]$Source, [string]$Destination)

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        $target = Join-Path $Destination $entry.Name
        if ($entry.PSIsContainer) {
            Copy-MissingSeedEntries -Source $entry.FullName -Destination $target
        }
        elseif (-not (Test-Path -LiteralPath $target)) {
            Copy-Item -LiteralPath $entry.FullName -Destination $target
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($ManagedRootSeed)) {
    Copy-MissingSeedEntries -Source $ManagedRootSeed -Destination $ManagedRoot
    Set-Content -LiteralPath (Join-Path $ManagedRoot ".cyantools-managed-root-seed") -Value ("managed-root-seed`t" + (Get-Date).ToUniversalTime().ToString("o")) -Encoding UTF8
}

$sharedScript = Join-Path $PSScriptRoot "Install-DiffusionPipeWsl.ps1"
if (-not (Test-Path -LiteralPath $sharedScript)) {
    throw "未找到共享 WSL 部署脚本：$sharedScript"
}



. $sharedScript -Action $Action -Mirror $Mirror -LocalProxyPort $LocalProxyPort -ManagedRoot $ManagedRoot -LibraryMode

$SdScriptsRepoUrl = "https://github.com/kohya-ss/sd-scripts.git"
$AnimaFastLoraRepoUrl = "https://github.com/GALIAIS/LoraHub.git"
$SdScriptsWindowsRepo = Join-Path $ManagedRoot "repos\sd-scripts"
$SdScriptsWindowsVenv = Join-Path $ManagedRoot "venvs\sd-scripts"
$AnimaFastLoraRepo = Join-Path $ManagedRoot "repos\anima-fast-lora"
$AnimaFastLoraTrainRoot = Join-Path $AnimaFastLoraRepo "external\anima_lora"
$WslSdScriptsRepo = '$HOME/sd-scripts'
$WslSdScriptsEnv = "wsl-sd-scripts-cu128"

New-Item -ItemType Directory -Force -Path $ManagedRoot | Out-Null

function Resolve-WindowsCommand {
    param(
        [string]$Name,
        [string[]]$FallbackPaths = @()
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    foreach ($candidate in $FallbackPaths) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    return $null
}

function Ensure-WindowsUv {
    $portableUv = Join-Path $ManagedRoot "tools\uv\uv.exe"
    $packagedUv = if ($RuntimeToolsRoot) { Join-Path $RuntimeToolsRoot "uv\uv.exe" } else { "" }
    $uv = Resolve-WindowsCommand -Name "uv.exe" -FallbackPaths @($packagedUv, $portableUv)
    if ($uv) {
        return $uv
    }

    Write-Step "下载便携版 uv"
    $uvRoot = Split-Path -Parent $portableUv
    $archive = Join-Path $ManagedRoot "downloads\uv-x86_64-pc-windows-msvc.zip"
    New-Item -ItemType Directory -Force -Path $uvRoot, (Split-Path -Parent $archive) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip" -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $uvRoot -Force
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $portableUv)) {
        throw "uv 下载完成，但未找到 $portableUv。"
    }
    return $portableUv
}

function Get-WindowsPipOfflineArgs {
    if ($OfflineWheelhouse) {
        return @("--no-index", "--find-links", $OfflineWheelhouse)
    }
    return @()
}

function Get-WindowsUvOfflineArgs {
    if ($OfflineWheelhouse) {
        return @("--no-index", "--find-links", $OfflineWheelhouse, "--no-sources")
    }
    return @()
}

function Get-CompatibleWindowsPython {
    $candidateSpecs = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @("ANIMA_PYTHON_310", "ANIMA_PYTHON")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $candidateSpecs.Add($value.Trim()) }
    }
    $extra = [Environment]::GetEnvironmentVariable("ANIMA_PYTHON_CANDIDATES")
    if (-not [string]::IsNullOrWhiteSpace($extra)) {
        foreach ($value in ($extra -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($value)) { $candidateSpecs.Add($value.Trim()) }
        }
    }
    if ($candidateSpecs.Count -eq 0) {
        $command = Get-Command "python.exe" -ErrorAction SilentlyContinue
        if ($command) { $candidateSpecs.Add($command.Source) }
    }
    $compatible = @()
    foreach ($spec in $candidateSpecs) {
        $parts = $spec -split '\|', 2
        $path = $parts[0].Trim()
        $declaredVersion = if ($parts.Count -gt 1) { $parts[1].Trim() } else { "" }
        try {
            $version = $declaredVersion
            if (-not $version) {
                $result = Invoke-Native -Label "读取 Python 版本" -Command @($path, "-c", "import sys; print('.'.join(map(str, sys.version_info[:3])))") -TimeoutSeconds 20 -Optional
                $version = ($result.StdOut + "`n" + $result.StdErr).Trim()
            }
            if ($version -match '^(3\.(10|11|12)\.\d+)') {
                $compatible += [pscustomobject]@{ Path = $path; Version = $Matches[1] }
            }
        }
        catch { }
    }
    foreach ($prefix in @("3.10", "3.11", "3.12")) {
        $match = $compatible | Where-Object { $_.Version.StartsWith($prefix) } | Select-Object -First 1
        if ($match) { return $match }
    }
    return $null
}

function Sync-WindowsRepository {
    param(
        [AllowEmptyString()][string]$Git,
        [string]$RepoUrl,
        [string]$RepoPath,
        [bool]$Reinstall,
        [bool]$UpdateOnly
    )

    if ($Reinstall -and (Test-Path -LiteralPath $RepoPath)) {
        Write-Step "清理旧仓库：$RepoPath"
        Remove-Item -LiteralPath $RepoPath -Recurse -Force
    }
    $archiveMarker = Join-Path $RepoPath ".anima-archive-revision"
    if (-not $Reinstall -and -not $UpdateOnly -and (Test-Path -LiteralPath $archiveMarker)) {
        Write-Host "复用包内仓库快照：$RepoPath"
        return
    }
    if ($UpdateOnly -and -not (Test-Path -LiteralPath (Join-Path $RepoPath ".git"))) {
        throw "$Backend 尚未安装，无法更新。请先点击安装。"
    }

    if ($Git -and (Test-Path -LiteralPath (Join-Path $RepoPath ".git"))) {
        Invoke-Native -Label "更新 $Backend 仓库" -Command @(
            $Git, "-C", $RepoPath, "-c", "http.version=HTTP/1.1", "pull", "--ff-only"
        ) -TimeoutSeconds 1800 | Out-Null
    }
    elseif ($Git) {
        if (Test-Path -LiteralPath $RepoPath) {
            Remove-Item -LiteralPath $RepoPath -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $RepoPath) | Out-Null
        Invoke-Native -Label "克隆 $Backend 仓库" -Command @(
            $Git, "-c", "http.version=HTTP/1.1", "-c", "http.lowSpeedLimit=0",
            "-c", "core.compression=0", "clone", "--filter=blob:none", "--single-branch",
            "--branch", "main", $RepoUrl, $RepoPath
        ) -TimeoutSeconds 1800 | Out-Null
    }
    else {
        Install-WindowsRepositoryArchive -RepoUrl $RepoUrl -RepoPath $RepoPath -RequestedRef "main"
    }
}

function Install-WindowsRepositoryArchive {
    param([string]$RepoUrl, [string]$RepoPath, [string]$RequestedRef)

    if ($RepoUrl -notmatch '^https://github\.com/([^/]+)/([^/]+?)(?:\.git)?$') {
        throw "未找到 Git，且当前仓库地址不支持 ZIP fallback。请安装 Git 后重试。"
    }
    $owner = $Matches[1]
    $repo = $Matches[2]
    $archiveUrl = "https://github.com/$owner/$repo/archive/$RequestedRef.zip"
    $parent = Split-Path -Parent $RepoPath
    $archive = Join-Path $parent ((Split-Path -Leaf $RepoPath) + ".zip")
    $extract = Join-Path $parent ((Split-Path -Leaf $RepoPath) + "-extract")
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    try {
        Write-Host "未找到 Git，使用 GitHub ZIP fallback：$archiveUrl"
        Invoke-WebRequest -UseBasicParsing -Uri $archiveUrl -OutFile $archive
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $extractRoot = [IO.Path]::GetFullPath($extract + [IO.Path]::DirectorySeparatorChar)
            foreach ($entry in $zip.Entries) {
                $target = [IO.Path]::GetFullPath((Join-Path $extract $entry.FullName))
                if (-not $target.StartsWith($extractRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "GitHub ZIP 包含不安全路径：$($entry.FullName)"
                }
            }
        }
        finally { $zip.Dispose() }
        Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
        $roots = @(Get-ChildItem -LiteralPath $extract -Directory)
        if ($roots.Count -ne 1) { throw "GitHub ZIP 目录结构异常：$archiveUrl" }
        Remove-Item -LiteralPath $RepoPath -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $roots[0].FullName -Destination $RepoPath
        Set-Content -LiteralPath (Join-Path $RepoPath ".anima-archive-revision") -Value "archive:$RequestedRef" -Encoding UTF8
    }
    finally {
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-WindowsPipMirrorArgs {
    if ($OfflineWheelhouse) {
        return @(Get-WindowsPipOfflineArgs)
    }
    if ($MirrorProfile.Pip) {
        return @("--index-url", [string]$MirrorProfile.Pip)
    }
    return @()
}

function Write-BackendStatusResult {
    param(
        [string]$BackendId,
        [string]$RuntimeTarget,
        [ValidateSet("available", "unavailable", "detected_but_unusable")][string]$State,
        [string]$Summary,
        [hashtable]$Details = @{}
    )

    $status = [ordered]@{
        backend_id = $BackendId
        runtime_target = $RuntimeTarget
        state = $State
        summary_zh = $Summary
        details = $Details
    }
    Write-Host $Summary
    if ($ResultPath) {
        $parent = Split-Path -Parent $ResultPath
        if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        $status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    }
    return [pscustomobject]$status
}

function Read-BackendManifest {
    param([string]$BackendId)
    $path = Join-Path $ManagedRoot "state\manifests\$BackendId.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { return $null }
}

function Get-WindowsRepositoryRevision {
    param([string]$RepoPath)
    $archiveMarker = Join-Path $RepoPath ".anima-archive-revision"
    if (Test-Path -LiteralPath $archiveMarker -PathType Leaf) {
        return (Get-Content -LiteralPath $archiveMarker -Raw -Encoding UTF8).Trim()
    }
    $git = Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @(
        (Join-Path $env:ProgramFiles "Git\cmd\git.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")
    )
    if (-not $git -or -not (Test-Path -LiteralPath (Join-Path $RepoPath ".git"))) { return "" }
    $result = Invoke-Native -Label "读取已安装仓库版本" -Command @($git, "-C", $RepoPath, "rev-parse", "HEAD") -TimeoutSeconds 5 -Optional
    return (($result.StdOut + "`n" + $result.StdErr).Trim())
}

function Get-WindowsBackendStatus {
    param(
        [string]$BackendId,
        [string]$Label,
        [string]$RepoPath,
        [string]$PythonPath,
        [string[]]$RequiredRelativePaths
    )
    $manifest = Read-BackendManifest $BackendId
    if (-not $manifest) {
        return Write-BackendStatusResult $BackendId "windows" "unavailable" "$Label 尚未安装。"
    }
    $missingCommands = @()
    if (-not (Test-Path -LiteralPath (Join-Path $RepoPath ".anima-archive-revision")) -and
        -not (Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @((Join-Path $env:ProgramFiles "Git\cmd\git.exe"), (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")))) {
        $missingCommands += "git"
    }
    $required = @($PythonPath) + @($RequiredRelativePaths | ForEach-Object { Join-Path $RepoPath $_ })
    $missingPaths = @($required | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingCommands.Count -gt 0 -or $missingPaths.Count -gt 0) {
        $allMissing = @($missingCommands) + @($missingPaths)
        return Write-BackendStatusResult $BackendId "windows" "detected_but_unusable" "$Label 安装缺少必要命令或文件：$($allMissing -join ', ')" @{
            missing_commands = $missingCommands
            missing_paths = $missingPaths
        }
    }
    try { $actualRevision = Get-WindowsRepositoryRevision $RepoPath }
    catch {
        return Write-BackendStatusResult $BackendId "windows" "detected_but_unusable" "$Label 版本检测超时或失败，请检查 Git 进程。" @{ drift_reason = "revision check failed" }
    }
    $expectedRevision = [string]$manifest.resolved_revision
    if (-not $actualRevision -or $actualRevision -ne $expectedRevision) {
        return Write-BackendStatusResult $BackendId "windows" "detected_but_unusable" "检测到 $Label 已安装版本与 manifest 记录不一致。" @{
            drift_reason = "revision mismatch"
            expected = $expectedRevision
            actual = $actualRevision
        }
    }
    return Write-BackendStatusResult $BackendId "windows" "available" "$Label 安装有效。"
}

function Save-BackendManifest {
    param(
        [string]$BackendId,
        [string]$RepoUrl,
        [string]$Revision,
        [string]$RuntimeTarget,
        [string]$InterpreterPath,
        [string]$InstallRoot
    )

    $manifestDir = Join-Path $ManagedRoot "state\manifests"
    New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
    $manifestPath = Join-Path $manifestDir "$BackendId.json"
    [ordered]@{
        backend_id = $BackendId
        repo_url = $RepoUrl
        requested_ref = "main"
        resolved_revision = $Revision
        runtime_target = $RuntimeTarget
        interpreter_path = $InterpreterPath
        install_root = $InstallRoot
        created_at = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Manifest saved: $manifestPath"
}

function Assert-PathsExist {
    param([string[]]$Paths, [string]$Label)
    $missing = @($Paths | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missing.Count -gt 0) {
        throw "$Label 缺少必要文件：$($missing -join ', ')"
    }
}

function Install-SdScriptsWindows {
    param([bool]$Reinstall = $false, [bool]$UpdateOnly = $false)

    Write-Host "安装镜像源：$($MirrorProfile.Label)"
    $git = Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @(
        (Join-Path $env:ProgramFiles "Git\cmd\git.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")
    )
    if ($Reinstall -and (Test-Path -LiteralPath $SdScriptsWindowsVenv)) {
        Remove-Item -LiteralPath $SdScriptsWindowsVenv -Recurse -Force
    }
    if ($Reinstall) {
        Remove-Item -LiteralPath (Join-Path $ManagedRoot "state\manifests\sd-scripts.json") -Force -ErrorAction SilentlyContinue
    }
    Sync-WindowsRepository -Git $git -RepoUrl $SdScriptsRepoUrl -RepoPath $SdScriptsWindowsRepo -Reinstall $Reinstall -UpdateOnly $UpdateOnly

    $interpreter = Get-CompatibleWindowsPython
    $interpreterManifest = ""
    if ($interpreter) {
        Write-Host "复用 Windows Python $($interpreter.Version): $($interpreter.Path)"
        Invoke-Native -Label "使用 Python $($interpreter.Version) 创建 sd-scripts 虚拟环境" -Command @(
            $interpreter.Path, "-m", "venv", "--clear", $SdScriptsWindowsVenv
        ) -TimeoutSeconds 1800 | Out-Null
        $interpreterManifest = $interpreter.Path
    }
    else {
        $uv = Ensure-WindowsUv
        $uvVenvArgs = @($uv, "venv", "--clear", "--python", "3.10")
        if ($OfflineWheelhouse) { $uvVenvArgs += "--offline" } else { $uvVenvArgs += "--seed" }
        $uvVenvArgs += $SdScriptsWindowsVenv
        Invoke-Native -Label "使用 uv 创建 sd-scripts Python 3.10 虚拟环境" -Command $uvVenvArgs -TimeoutSeconds 1800 | Out-Null
        $interpreterManifest = "uv:3.10"
        if ($OfflineWheelhouse) {
            Invoke-Native -Label "离线安装 sd-scripts 虚拟环境基础工具" -Command @(
                $uv, "pip", "install", "--python", (Join-Path $SdScriptsWindowsVenv "Scripts\python.exe"),
                "--no-index", "--find-links", $OfflineWheelhouse, "--offline", "pip", "setuptools", "wheel"
            ) -TimeoutSeconds 1800 | Out-Null
        }
    }
    $python = Join-Path $SdScriptsWindowsVenv "Scripts\python.exe"
    $pipMirror = @(Get-WindowsPipMirrorArgs)

    Invoke-Native -Label "安装 sd-scripts Python 依赖" -Command (@(
        $python, "-m", "pip", "install", "--retries", "5", "--timeout", "60"
    ) + $pipMirror + @("-r", (Join-Path $SdScriptsWindowsRepo "requirements.txt"))) -TimeoutSeconds 0 | Out-Null
    $torchIndexArgs = if ($OfflineWheelhouse) { @(Get-WindowsPipOfflineArgs) } else { @("--index-url", "https://download.pytorch.org/whl/cu128") }
    Invoke-Native -Label "安装 CUDA 版 PyTorch / torchvision" -Command (@(
        $python, "-m", "pip", "install", "--force-reinstall",
        "torch==2.10.0+cu128", "torchvision==0.25.0+cu128", "torchaudio==2.10.0+cu128"
    ) + $torchIndexArgs) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 xformers 加速依赖" -Command (@(
        $python, "-m", "pip", "install", "--force-reinstall", "--no-deps",
        "xformers==0.0.35"
    ) + $torchIndexArgs) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 Triton Windows 加速依赖" -Command (@(
        $python, "-m", "pip", "install", "--upgrade"
    ) + $pipMirror + @("triton-windows==3.6.0.post26")) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 LyCORIS 训练依赖" -Command (@(
        $python, "-m", "pip", "install", "--upgrade"
    ) + $pipMirror + @("lycoris-lora")) -TimeoutSeconds 0 | Out-Null

    Validate-SdScriptsWindows
    Save-BackendManifest -BackendId "sd-scripts" -RepoUrl $SdScriptsRepoUrl -Revision (Get-WindowsRepositoryRevision $SdScriptsWindowsRepo) -RuntimeTarget "windows" -InterpreterPath $interpreterManifest -InstallRoot $ManagedRoot
}

function Validate-SdScriptsWindows {
    $python = Join-Path $SdScriptsWindowsVenv "Scripts\python.exe"
    Assert-PathsExist -Label "sd-scripts Windows 安装" -Paths @(
        $python,
        (Join-Path $SdScriptsWindowsRepo "README.md"),
        (Join-Path $SdScriptsWindowsRepo "requirements.txt"),
        (Join-Path $SdScriptsWindowsRepo "train_network.py"),
        (Join-Path $SdScriptsWindowsRepo "sdxl_train_network.py"),
        (Join-Path $SdScriptsWindowsRepo "anima_train_network.py")
    )
    Invoke-Native -Label "校验 sd-scripts Windows Python 环境" -Command @(
        $python, "-c", "import accelerate, torch; print('torch', torch.__version__); print('accelerate', accelerate.__version__)"
    ) -TimeoutSeconds 120 | Out-Null
    Invoke-Native -Label "校验 sd-scripts 训练入口" -Command @($python, (Join-Path $SdScriptsWindowsRepo "train_network.py"), "--help") -TimeoutSeconds 120 | Out-Null
    Write-Host "sd-scripts Windows 安装有效。"
}

function Show-SdScriptsWindowsStatus {
    $python = Join-Path $SdScriptsWindowsVenv "Scripts\python.exe"
    Get-WindowsBackendStatus -BackendId "sd-scripts" -Label "sd-scripts Windows" -RepoPath $SdScriptsWindowsRepo -PythonPath $python -RequiredRelativePaths @(
        "README.md", "requirements.txt", "setup.py", "train_network.py", "sdxl_train_network.py",
        "sd3_train_network.py", "flux_train_network.py", "lumina_train_network.py",
        "hunyuan_image_train_network.py", "anima_train.py", "anima_train_network.py"
    ) | Out-Null
}

function Ensure-AnimaFastWindowsUvEnvironment {
    $pyproject = Join-Path $AnimaFastLoraTrainRoot "pyproject.toml"
    if (-not (Test-Path -LiteralPath $pyproject)) {
        return
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($pyproject)) {
        [void]$lines.Add($line)
    }
    $environmentLine = 'environments = ["sys_platform == ''win32''"]'
    $toolUvIndex = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Trim() -eq "[tool.uv]") {
            $toolUvIndex = $index
            break
        }
    }
    if ($toolUvIndex -lt 0) {
        [void]$lines.Add("")
        [void]$lines.Add("[tool.uv]")
        [void]$lines.Add($environmentLine)
    }
    else {
        $sectionEnd = $lines.Count
        for ($index = $toolUvIndex + 1; $index -lt $lines.Count; $index++) {
            if ($lines[$index].TrimStart().StartsWith("[")) {
                $sectionEnd = $index
                break
            }
        }
        $replaced = $false
        for ($index = $toolUvIndex + 1; $index -lt $sectionEnd; $index++) {
            if ($lines[$index].TrimStart().StartsWith("environments")) {
                $lines[$index] = $environmentLine
                $replaced = $true
                break
            }
        }
        if (-not $replaced) {
            $lines.Insert($toolUvIndex + 1, $environmentLine)
        }
    }
    [IO.File]::WriteAllLines($pyproject, $lines, [Text.UTF8Encoding]::new($false))
}

function Install-AnimaFastLoraWindows {
    param([bool]$Reinstall = $false, [bool]$UpdateOnly = $false)

    Write-Host "安装镜像源：$($MirrorProfile.Label)"
    $git = Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @(
        (Join-Path $env:ProgramFiles "Git\cmd\git.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")
    )
    $uv = Ensure-WindowsUv
    if ($Reinstall) {
        Remove-Item -LiteralPath (Join-Path $ManagedRoot "state\manifests\anima-fast-lora.json") -Force -ErrorAction SilentlyContinue
    }
    Sync-WindowsRepository -Git $git -RepoUrl $AnimaFastLoraRepoUrl -RepoPath $AnimaFastLoraRepo -Reinstall $Reinstall -UpdateOnly $UpdateOnly
    Ensure-AnimaFastWindowsUvEnvironment

    $uvArgs = @($uv, "sync", "--project", $AnimaFastLoraTrainRoot, "--python", "3.13", "--no-dev", "--no-default-groups") + @(Get-WindowsUvOfflineArgs)
    if ($OfflineWheelhouse) {
        $env:UV_CACHE_DIR = (Join-Path (Split-Path -Parent $OfflineWheelhouse) "uv-cache")
        $env:UV_LINK_MODE = "copy"
    }
    if ($MirrorProfile.Pip) {
        $env:UV_DEFAULT_INDEX = [string]$MirrorProfile.Pip
    }
    try {
        Invoke-Native -Label "安装 Anima Fast LoRA Python 3.13 依赖" -Command $uvArgs -TimeoutSeconds 0 | Out-Null
    }
    finally {
        Remove-Item Env:\UV_DEFAULT_INDEX -ErrorAction SilentlyContinue
        Remove-Item Env:\UV_CACHE_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:\UV_LINK_MODE -ErrorAction SilentlyContinue
    }

    Validate-AnimaFastLoraWindows
    $python = Join-Path $AnimaFastLoraTrainRoot ".venv\Scripts\python.exe"
    Save-BackendManifest -BackendId "anima-fast-lora" -RepoUrl $AnimaFastLoraRepoUrl -Revision (Get-WindowsRepositoryRevision $AnimaFastLoraRepo) -RuntimeTarget "windows" -InterpreterPath $python -InstallRoot $ManagedRoot
}

function Validate-AnimaFastLoraWindows {
    $python = Join-Path $AnimaFastLoraTrainRoot ".venv\Scripts\python.exe"
    Assert-PathsExist -Label "Anima Fast LoRA Windows 安装" -Paths @(
        $python,
        (Join-Path $AnimaFastLoraTrainRoot "README.md"),
        (Join-Path $AnimaFastLoraTrainRoot "pyproject.toml"),
        (Join-Path $AnimaFastLoraTrainRoot "train.py"),
        (Join-Path $AnimaFastLoraTrainRoot "configs\base.toml"),
        (Join-Path $AnimaFastLoraTrainRoot "configs\gui-methods\lora.toml")
    )
    Invoke-Native -Label "校验 Anima Fast LoRA Python 环境" -Command @($python, "--version") -TimeoutSeconds 60 | Out-Null
    Write-Host "Anima Fast LoRA Windows 安装有效。"
}

function Show-AnimaFastLoraWindowsStatus {
    $python = Join-Path $AnimaFastLoraTrainRoot ".venv\Scripts\python.exe"
    Get-WindowsBackendStatus -BackendId "anima-fast-lora" -Label "Anima Fast LoRA Windows" -RepoPath $AnimaFastLoraRepo -PythonPath $python -RequiredRelativePaths @(
        "external\anima_lora\README.md", "external\anima_lora\pyproject.toml",
        "external\anima_lora\train.py", "external\anima_lora\configs\base.toml",
        "external\anima_lora\configs\gui-methods\lora.toml"
    ) | Out-Null
}

function Remove-WslSdScriptsEnvironment {
    $conda = Get-CondaSourceSnippet
    Invoke-WslBash -Label "清理 wsl-sd-scripts conda 环境" -Script "$conda && conda env remove -n $WslSdScriptsEnv -y || true" | Out-Null
}

function Sync-WslSdScriptsRepository {
    param([bool]$Reinstall = $false, [bool]$UpdateOnly = $false)

    if ($Reinstall) {
        Invoke-WslBash -Label "清理 WSL sd-scripts 仓库" -Script "rm -rf $WslSdScriptsRepo" | Out-Null
        Remove-WslSdScriptsEnvironment
    }
    if ($UpdateOnly) {
        Invoke-WslBash -Label "检查 WSL sd-scripts 仓库" -Script "test -d $WslSdScriptsRepo/.git" -TimeoutSeconds 30 | Out-Null
    }

    $exists = $false
    try {
        Invoke-WslBash -Label "检查 WSL sd-scripts 仓库" -Script "test -d $WslSdScriptsRepo/.git" -TimeoutSeconds 30 | Out-Null
        $exists = $true
    }
    catch { }

    if ($exists) {
        Invoke-WslGit -Label "更新 WSL sd-scripts 仓库" -GitArgs "-C $WslSdScriptsRepo -c http.version=HTTP/1.1 pull --ff-only"
    }
    else {
        Invoke-WslGit -Label "克隆 WSL sd-scripts 仓库" -GitArgs "-c http.version=HTTP/1.1 -c http.lowSpeedLimit=0 -c core.compression=0 clone --filter=blob:none --single-branch --branch main $SdScriptsRepoUrl $WslSdScriptsRepo"
    }
}

function Install-WslSdScriptsDependencies {
    Ensure-Miniconda
    $conda = Get-CondaSourceSnippet
    $proxy = Get-ProxyEnvScript
    $condaChannels = Get-CondaDefaultChannels
    $pipArgs = Get-PipIndexArgs
    $pipInstall = ("anima_pip_install " + $pipArgs).Trim()
    $pipRetry = Get-PipRetryFunction
    $script = @"
set -e
$proxy
$conda
export CONDA_PLUGINS_AUTO_ACCEPT_TOS=yes
conda create -n $WslSdScriptsEnv python=3.10 pysocks -y $condaChannels || true
conda install -n $WslSdScriptsEnv pysocks -y $condaChannels || true
conda activate $WslSdScriptsEnv
cd $WslSdScriptsRepo
mkdir -p "`$HOME/.cache/anima-pip" "`$HOME/tmp"
export TMPDIR="`$HOME/tmp" PIP_CACHE_DIR="`$HOME/.cache/anima-pip"
$pipRetry
python -m pip install --index-url https://download.pytorch.org/whl/cu128 torch==2.10.0+cu128 torchvision==0.25.0+cu128 torchaudio==2.10.0+cu128
$pipInstall -r requirements.txt
$pipInstall --upgrade lycoris-lora
python -m accelerate.commands.accelerate_cli config default --mixed_precision bf16 || true
python - <<'PY'
import accelerate, torch
print('torch', torch.__version__)
print('accelerate', accelerate.__version__)
PY
"@
    Invoke-WslBash -Label "安装 wsl-sd-scripts Python / CUDA 依赖" -Script $script -TimeoutSeconds 0 | Out-Null
}

function Run-WslSdScriptsSmoke {
    $conda = Get-CondaSourceSnippet
    $script = @"
set -e
cd $WslSdScriptsRepo
$conda
conda activate $WslSdScriptsEnv
SMOKE_ROOT="`$HOME/.cache/anima-wsl-sd-scripts-smoke"
rm -rf "`$SMOKE_ROOT"
mkdir -p "`$SMOKE_ROOT/data/1_smoke" "`$SMOKE_ROOT/output"
python train_network.py --help >/dev/null
python - <<'PY'
from pathlib import Path
from PIL import Image

root = Path.home() / ".cache" / "anima-wsl-sd-scripts-smoke"
data_dir = root / "data" / "1_smoke"
output_dir = root / "output"
data_dir.mkdir(parents=True, exist_ok=True)
output_dir.mkdir(parents=True, exist_ok=True)
Image.new("RGB", (16, 16), (80, 120, 200)).save(data_dir / "sample.png")
(data_dir / "sample.txt").write_text("smoke sample\n", encoding="utf-8")
(root / "smoke_train.py").write_text(
    """
from pathlib import Path
import torch
from torch import nn
from PIL import Image
from torchvision import transforms

root = Path.home() / ".cache" / "anima-wsl-sd-scripts-smoke"
image = Image.open(root / "data" / "1_smoke" / "sample.png").convert("RGB")
tensor = transforms.ToTensor()(image).flatten().unsqueeze(0)
model = nn.Linear(tensor.shape[1], 1)
optimizer = torch.optim.SGD(model.parameters(), lr=0.01)
for epoch in range(1):
    optimizer.zero_grad(set_to_none=True)
    loss = model(tensor).square().mean()
    loss.backward()
    optimizer.step()
torch.save({"epoch": 1, "steps": 1, "loss": float(loss.detach())}, root / "output" / "smoke_state.pt")
print("wsl-sd-scripts smoke epoch=1 step=1 loss=", float(loss.detach()))
""".strip() + "\n",
    encoding="utf-8",
)
PY
python -m accelerate.commands.accelerate_cli launch --num_processes 1 --num_machines 1 --mixed_precision no --dynamo_backend no "`$SMOKE_ROOT/smoke_train.py"
test -f "`$SMOKE_ROOT/output/smoke_state.pt"
echo "wsl-sd-scripts 1 step smoke training finished."
"@
    Invoke-WslBash -Label "运行 wsl-sd-scripts 1 step / 1 epoch smoke 训练" -Script $script -TimeoutSeconds 0 | Out-Null
}

function Validate-WslSdScripts {
    if (-not (Test-Wsl2Available) -or -not (Test-UbuntuInstalled)) {
        throw "wsl-sd-scripts 需要可用的 WSL2 / Ubuntu / NVIDIA WSL 环境。"
    }
    $conda = Get-CondaSourceSnippet
    $script = @"
set -e
cd $WslSdScriptsRepo
command -v git
test -f train_network.py
test -f requirements.txt
$conda
conda activate $WslSdScriptsEnv
python --version
python train_network.py --help >/dev/null
python - <<'PY'
import accelerate, torch
print('torch', torch.__version__)
print('accelerate', accelerate.__version__)
PY
"@
    Invoke-WslBash -Label "校验 wsl-sd-scripts" -Script $script -TimeoutSeconds 180 | Out-Null
    Run-WslSdScriptsSmoke
    Write-Host "wsl-sd-scripts 环境校验完成，1 step / 1 epoch smoke 已通过。"
}

function Install-WslSdScripts {
    param([bool]$Reinstall = $false, [bool]$UpdateOnly = $false)

    Write-Host "安装镜像源：$($MirrorProfile.Label)"
    if (-not (Test-Wsl2Available) -or -not (Test-UbuntuInstalled)) {
        throw "wsl-sd-scripts 需要可用的 WSL2 / Ubuntu。请先安装 WSL 和 Ubuntu 子系统。"
    }
    Ensure-UbuntuRootInitialized
    Install-WslPrerequisites
    Sync-WslSdScriptsRepository -Reinstall $Reinstall -UpdateOnly $UpdateOnly
    Install-WslSdScriptsDependencies
    Validate-WslSdScripts
    $revisionResult = Invoke-WslBash -Label "记录 wsl-sd-scripts 当前版本" -Script "git -C $WslSdScriptsRepo rev-parse HEAD" -TimeoutSeconds 60
    $revision = (($revisionResult.StdOut + "`n" + $revisionResult.StdErr).Trim())
    Save-BackendManifest -BackendId "wsl-sd-scripts" -RepoUrl $SdScriptsRepoUrl -Revision $revision -RuntimeTarget "wsl2" -InterpreterPath "wsl.exe conda:$WslSdScriptsEnv" -InstallRoot "~/sd-scripts"
}

function Show-WslSdScriptsStatus {
    Get-WslBackendStatus -BackendId "wsl-sd-scripts" -Label "wsl-sd-scripts" -RepoPath $WslSdScriptsRepo -CondaEnv $WslSdScriptsEnv -RequiredRelativePaths @(
        "README.md", "requirements.txt", "setup.py", "train_network.py", "sdxl_train_network.py",
        "sd3_train_network.py", "flux_train_network.py", "lumina_train_network.py",
        "hunyuan_image_train_network.py", "anima_train_network.py"
    ) | Out-Null
}

function Get-WslBackendStatus {
    param(
        [string]$BackendId,
        [string]$Label,
        [string]$RepoPath,
        [string]$CondaEnv,
        [string[]]$RequiredRelativePaths
    )
    if (-not (Test-Wsl2Available) -or -not (Test-UbuntuInstalled)) {
        return Write-BackendStatusResult $BackendId "wsl2" "detected_but_unusable" "$Label 需要可用的 WSL2 / Ubuntu / NVIDIA WSL 训练环境。"
    }
    $manifest = Read-BackendManifest $BackendId
    if (-not $manifest) {
        return Write-BackendStatusResult $BackendId "wsl2" "unavailable" "$Label 尚未安装。"
    }
    $conda = Get-CondaSourceSnippet
    $quotedPaths = @($RequiredRelativePaths | ForEach-Object { "'$RepoPath/$_'" }) -join " "
    $script = @"
set +e
missing_commands=''
missing_paths=''
command -v git >/dev/null 2>&1 || missing_commands='git'
$conda >/dev/null 2>&1 || missing_commands="`$missing_commands conda"
for required_path in $quotedPaths; do
  test -f "`$required_path" || missing_paths="`$missing_paths `$required_path"
done
conda run -n $CondaEnv python --version >/dev/null 2>&1 || missing_paths="`$missing_paths conda:$CondaEnv"
printf 'CYANTOOLS_MISSING_COMMANDS=%s\n' "`$missing_commands"
printf 'CYANTOOLS_MISSING_PATHS=%s\n' "`$missing_paths"
"@
    try {
        $result = Invoke-WslBash -Label "读取 $Label 结构化状态" -Script $script -TimeoutSeconds 30
        $text = $result.StdOut + "`n" + $result.StdErr
        $commands = @()
        $paths = @()
        if ($text -match 'CYANTOOLS_MISSING_COMMANDS=([^\r\n]*)') { $commands = @($Matches[1].Trim() -split '\s+' | Where-Object { $_ }) }
        if ($text -match 'CYANTOOLS_MISSING_PATHS=([^\r\n]*)') { $paths = @($Matches[1].Trim() -split '\s+' | Where-Object { $_ }) }
        if ($commands.Count -gt 0 -or $paths.Count -gt 0) {
            return Write-BackendStatusResult $BackendId "wsl2" "detected_but_unusable" "$Label 安装缺少必要命令或文件：$((@($commands) + @($paths)) -join ', ')" @{
                missing_commands = $commands
                missing_paths = $paths
            }
        }
    }
    catch {
        return Write-BackendStatusResult $BackendId "wsl2" "detected_but_unusable" "$Label 环境检测失败：$($_.Exception.Message)" @{ detect_error = $_.Exception.Message }
    }
    return Write-BackendStatusResult $BackendId "wsl2" "available" "$Label 环境可用。" @{
        repo_dir = $RepoPath
        conda_env_name = $CondaEnv
    }
}

function Show-WslBaseStatus {
    Write-Host "托管目录：$ManagedRoot"
    if (-not (Test-WslExe)) {
        Write-BackendStatusResult "wsl" "wsl2" "unavailable" "未检测到 Windows WSL 命令，请先安装 WSL。" @{ supports_wsl_install = $true; is_wsl_base = $true; distro_name = "Ubuntu" } | Out-Null
        return
    }
    Invoke-Native -Label "WSL 发行版状态" -Command @("wsl.exe", "-l", "-v") -TimeoutSeconds 30 -NativeOutputEncoding ([Text.Encoding]::Unicode) -Optional | Out-Null
    $details = @{ supports_wsl_install = $true; is_wsl_base = $true; distro_name = "Ubuntu" }
    if (-not (Test-Wsl2Available)) {
        Write-BackendStatusResult "wsl" "wsl2" "unavailable" "未检测到可用的 WSL2 发行版，请先安装 WSL 和 Ubuntu 子系统。" $details | Out-Null
    }
    elseif (-not (Test-UbuntuInstalled)) {
        Write-BackendStatusResult "wsl" "wsl2" "unavailable" "WSL2 已可用，但尚未检测到 Ubuntu 子系统。" $details | Out-Null
    }
    elseif (-not (Test-UbuntuRootReady)) {
        Write-BackendStatusResult "wsl" "wsl2" "detected_but_unusable" "Ubuntu 子系统已安装，但 root 非交互启动尚未就绪。" $details | Out-Null
    }
    else {
        Write-BackendStatusResult "wsl" "wsl2" "available" "WSL2 / Ubuntu 基础环境可用。" $details | Out-Null
    }
}

function Invoke-WslBaseAction {
    switch ($Action) {
        "Check" { Show-WslBaseStatus }
        "InstallWsl" { Install-WindowsWslFeature }
        "InstallUbuntu" { Install-UbuntuDistribution -Reinstall:$ForceReinstallDistribution }
        "Install" {
            Install-WindowsWslFeature
            if (Test-WslExe) { Install-UbuntuDistribution }
        }
        "Update" { Install-WindowsWslFeature; if (Test-WslExe) { Install-UbuntuDistribution } }
        "Reinstall" { Install-UbuntuDistribution -Reinstall:$true }
        "Validate" {
            if (-not (Test-Wsl2Available) -or -not (Test-UbuntuInstalled) -or -not (Test-UbuntuRootReady)) {
                throw "WSL2 / Ubuntu 基础环境不可用。"
            }
            Write-Host "WSL2 / Ubuntu 基础环境可用。"
        }
        "DeleteUbuntu" {
            if (Test-UbuntuInstalled) {
                Stop-UbuntuDistribution
                Unregister-UbuntuDistribution
                Wait-UbuntuRemoved
                Write-Host "Ubuntu 子系统已删除。"
            }
            else { Write-Host "未检测到 Ubuntu WSL 发行版，无需删除。" }
        }
        default { throw "WSL 基础环境不支持操作：$Action" }
    }
}

try {
    Write-Host "CyanTools training backend deployment"
    Write-Host "Backend: $Backend"
    Write-Host "Action: $Action"
    Write-Host "Mirror: $($MirrorProfile.Label)"
    Write-Host "Managed root: $ManagedRoot"

    switch ($Backend) {
        "wsl" { Invoke-WslBaseAction }
        "diffusion-pipe" {
            switch ($Action) {
                "Check" {
                    Get-WslBackendStatus -BackendId "diffusion-pipe" -Label "diffusion-pipe" -RepoPath '$HOME/diffusion-pipe' -CondaEnv "diffusion-pipe-cu128" -RequiredRelativePaths @(
                        "README.md", ".gitmodules", "requirements.txt", "train.py"
                    ) | Out-Null
                }
                "InstallWsl" { Install-WindowsWslFeature }
                "InstallUbuntu" { Install-UbuntuDistribution -Reinstall:$ForceReinstallDistribution }
                "Install" { Install-DiffusionPipe }
                "Update" { Install-DiffusionPipe -UpdateOnly:$true }
                "Reinstall" { Install-DiffusionPipe -Reinstall:$true }
                "Validate" { Validate-DiffusionPipe }
                "DeleteUbuntu" { Invoke-WslBaseAction }
            }
        }
        "sd-scripts" {
            switch ($Action) {
                "Check" { Show-SdScriptsWindowsStatus }
                "Install" { Install-SdScriptsWindows }
                "Update" { throw "sd-scripts Windows 暂不支持更新，请使用重装。" }
                "Reinstall" { Install-SdScriptsWindows -Reinstall:$true }
                "Validate" { Validate-SdScriptsWindows }
                default { throw "sd-scripts Windows 不支持操作：$Action" }
            }
        }
        "anima-fast-lora" {
            switch ($Action) {
                "Check" { Show-AnimaFastLoraWindowsStatus }
                "Install" { Install-AnimaFastLoraWindows }
                "Update" { throw "Anima Fast LoRA Windows 暂不支持更新，请使用重装。" }
                "Reinstall" { Install-AnimaFastLoraWindows -Reinstall:$true }
                "Validate" { Validate-AnimaFastLoraWindows }
                default { throw "Anima Fast LoRA Windows 不支持操作：$Action" }
            }
        }
        "wsl-sd-scripts" {
            switch ($Action) {
                "Check" { Show-WslSdScriptsStatus }
                "Install" { Install-WslSdScripts }
                "Update" { Install-WslSdScripts -UpdateOnly:$true }
                "Reinstall" { Install-WslSdScripts -Reinstall:$true }
                "Validate" { Validate-WslSdScripts }
                default { throw "wsl-sd-scripts 不支持操作：$Action" }
            }
        }
    }

    Write-Host "Action completed: $Backend / $Action"
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) {
        Write-Error $_.InvocationInfo.PositionMessage -ErrorAction Continue
    }
    if ($_.ScriptStackTrace) {
        Write-Error $_.ScriptStackTrace -ErrorAction Continue
    }
    exit 1
}
