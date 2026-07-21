param(
    [ValidateSet("Check", "InstallWsl", "InstallUbuntu", "Install", "Update", "Reinstall", "Validate", "DeleteUbuntu")]
    [string]$Action = "Check",

    [ValidateSet("default", "tsinghua", "aliyun")]
    [string]$Mirror = "default",

    [int]$LocalProxyPort = 0,

    [string]$ManagedRoot = "",

    [switch]$LibraryMode
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$RepoUrl = "https://github.com/tdrussell/diffusion-pipe.git"
$RepoRef = "main"
$Distro = "Ubuntu"
$UbuntuLauncher = "ubuntu.exe"
$RepoDir = "`$HOME/diffusion-pipe"
$CondaEnvName = "diffusion-pipe-cu128"
$TorchVersion = "2.9.0"
$CudaTag = "cu128"
$TorchVisionVersion = "0.24.0"
$TorchAudioVersion = "2.9.0"
$FlashAttnVersion = "2.8.3"
$RequiredSubmodules = @("submodules/ComfyUI", "submodules/HunyuanVideo")
$UbuntuRootfsUrls = @(
    "https://cloud-images.ubuntu.com/wsl/releases/24.04/current/ubuntu-noble-wsl-amd64-wsl.rootfs.tar.gz",
    "https://cloud-images.ubuntu.com/wsl/releases/24.04/current/ubuntu-noble-wsl-amd64-24.04lts.rootfs.tar.gz"
)

if ([string]::IsNullOrWhiteSpace($ManagedRoot)) {
    $ManagedRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "CyanTools\diffusion-pipe-wsl"
}

$MirrorProfiles = @{
    default = @{
        Label = "官方源"
        Pip = $null
        Conda = $null
        Apt = $null
    }
    tsinghua = @{
        Label = "清华 TUNA"
        Pip = "https://pypi.tuna.tsinghua.edu.cn/simple"
        Conda = "https://mirrors.tuna.tsinghua.edu.cn/anaconda"
        Apt = "https://mirrors.tuna.tsinghua.edu.cn/ubuntu/"
    }
    aliyun = @{
        Label = "阿里云"
        Pip = "https://mirrors.aliyun.com/pypi/simple/"
        Conda = "https://mirrors.aliyun.com/anaconda"
        Apt = "https://mirrors.aliyun.com/ubuntu/"
    }
}

New-Item -ItemType Directory -Force -Path $ManagedRoot | Out-Null
$MirrorProfile = $MirrorProfiles[$Mirror]

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message"
}

function Quote-NativeArgument {
    param([AllowNull()][string]$Argument)

    if ($null -eq $Argument -or $Argument.Length -eq 0) {
        return '""'
    }
    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($ch in $Argument.ToCharArray()) {
        if ($ch -eq '\') {
            $backslashes++
        }
        elseif ($ch -eq '"') {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * ($backslashes * 2)))
                $backslashes = 0
            }
            [void]$builder.Append('\"')
        }
        else {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * $backslashes))
                $backslashes = 0
            }
            [void]$builder.Append($ch)
        }
    }
    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * ($backslashes * 2)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Join-NativeArguments {
    param([string[]]$Arguments)

    if ($null -eq $Arguments -or $Arguments.Count -eq 0) {
        return ""
    }

    return ($Arguments | ForEach-Object { Quote-NativeArgument $_ }) -join " "
}

function Format-CommandLine {
    param([string[]]$Command)

    return Join-NativeArguments $Command
}

function Test-Utf16LittleEndianBytes {
    param([byte[]]$Bytes)

    if ($null -eq $Bytes -or $Bytes.Length -lt 2) {
        return $false
    }

    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) {
        return $true
    }

    $pairs = [Math]::Min([int]($Bytes.Length / 2), 4096)
    if ($pairs -eq 0) {
        return $false
    }

    $oddZeroCount = 0
    $evenZeroCount = 0
    for ($index = 0; $index -lt $pairs; $index++) {
        if ($Bytes[($index * 2) + 1] -eq 0) {
            $oddZeroCount++
        }
        if ($Bytes[($index * 2)] -eq 0) {
            $evenZeroCount++
        }
    }

    return ($oddZeroCount -ge [Math]::Max(2, [int]($pairs * 0.08))) -and ($oddZeroCount -gt ($evenZeroCount * 3))
}

function Convert-NativeOutputBytesToString {
    param(
        [byte[]]$Bytes,
        [System.Text.Encoding]$PreferredEncoding = [System.Text.Encoding]::UTF8
    )

    if ($null -eq $Bytes -or $Bytes.Length -eq 0) {
        return ""
    }

    if (Test-Utf16LittleEndianBytes -Bytes $Bytes) {
        return [System.Text.Encoding]::Unicode.GetString($Bytes)
    }

    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        return [System.Text.Encoding]::UTF8.GetString($Bytes)
    }

    if ($PreferredEncoding -and $PreferredEncoding.CodePage -ne [System.Text.Encoding]::UTF8.CodePage) {
        return $PreferredEncoding.GetString($Bytes)
    }

    try {
        $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
        return $strictUtf8.GetString($Bytes)
    }
    catch {
        return [System.Text.Encoding]::Default.GetString($Bytes)
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string[]]$Command,

        [int]$TimeoutSeconds = 0,

        [System.Text.Encoding]$NativeOutputEncoding = [System.Text.Encoding]::UTF8,

        [switch]$Optional
    )

    Write-Step $Label
    Write-Host ("$ " + (Format-CommandLine $Command))

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Command[0]
    if ($Command.Count -gt 1) {
        $psi.Arguments = Join-NativeArguments $Command[1..($Command.Count - 1)]
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = $NativeOutputEncoding
    $psi.StandardErrorEncoding = $NativeOutputEncoding

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $stdoutBuffer = [System.IO.MemoryStream]::new()
    $stderrBuffer = [System.IO.MemoryStream]::new()

    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutBuffer)
        $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrBuffer)

        if ($TimeoutSeconds -gt 0) {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                try {
                    $process.Kill($true)
                }
                catch {
                    try { $process.Kill() } catch { }
                }
                throw "$Label 超时（$TimeoutSeconds 秒）。"
            }
        }
        else {
            $process.WaitForExit()
        }

        $process.WaitForExit()
        $stdoutTask.GetAwaiter().GetResult()
        $stderrTask.GetAwaiter().GetResult()
        $stdoutText = Convert-NativeOutputBytesToString -Bytes $stdoutBuffer.ToArray() -PreferredEncoding $NativeOutputEncoding
        $stderrText = Convert-NativeOutputBytesToString -Bytes $stderrBuffer.ToArray() -PreferredEncoding $NativeOutputEncoding
        if (-not [string]::IsNullOrWhiteSpace($stdoutText)) {
            Write-Host $stdoutText.TrimEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
            Write-Host $stderrText.TrimEnd()
        }

        if ($process.ExitCode -ne 0 -and -not $Optional) {
            $detail = $stderrText.Trim()
            if ([string]::IsNullOrWhiteSpace($detail)) {
                $detail = $stdoutText.Trim()
            }
            throw "$Label 失败，退出码 $($process.ExitCode)。$detail"
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdoutText
            StdErr = $stderrText
        }
    }
    finally {
        $stdoutBuffer.Dispose()
        $stderrBuffer.Dispose()
        $process.Dispose()
    }
}

function Invoke-OptionalNative {
    param(
        [string]$Label,
        [string[]]$Command,
        [int]$TimeoutSeconds = 0
    )

    try {
        Invoke-Native -Label $Label -Command $Command -TimeoutSeconds $TimeoutSeconds -Optional | Out-Null
    }
    catch {
        Write-Host "$Label 未完成，继续尝试后续步骤。$($_.Exception.Message)"
    }
}

function Escape-WslBashScript {
    param([string]$Script)
    return $Script.Replace('$', '\$')
}

function Invoke-WslBash {
    param(
        [string]$Label,
        [string]$Script,
        [int]$TimeoutSeconds = 0
    )

    return Invoke-Native -Label $Label -Command @(
        "wsl.exe",
        "-d",
        $Distro,
        "-u",
        "root",
        "--",
        "bash",
        "-lc",
        (Escape-WslBashScript $Script)
    ) -TimeoutSeconds $TimeoutSeconds
}

function Get-CondaSourceSnippet {
    return @'
{ anima_conda_found=''; for anima_conda_sh in "$HOME/miniconda3/etc/profile.d/conda.sh" "$HOME/anaconda3/etc/profile.d/conda.sh" "/root/miniconda3/etc/profile.d/conda.sh" "/opt/conda/etc/profile.d/conda.sh"; do if [ -f "$anima_conda_sh" ]; then . "$anima_conda_sh"; anima_conda_found=1; break; fi; done; test -n "$anima_conda_found" && command -v conda >/dev/null 2>&1; }
'@
}

function Get-ProxyEnvScript {
    if ($LocalProxyPort -le 0) {
        return ""
    }

    return @'
anima_wsl_host=$(ip route | awk '/^default/ {print $3; exit}')
if [ -n "$anima_wsl_host" ]; then
  anima_wsl_proxy="http://$anima_wsl_host:__PORT__"
  export http_proxy="$anima_wsl_proxy"
  export https_proxy="$anima_wsl_proxy"
  export HTTP_PROXY="$anima_wsl_proxy"
  export HTTPS_PROXY="$anima_wsl_proxy"
  export all_proxy="$anima_wsl_proxy"
  export ALL_PROXY="$anima_wsl_proxy"
  export no_proxy="localhost,127.0.0.1,::1"
  export NO_PROXY="$no_proxy"
  echo "WSL dependency downloads will use the local Windows proxy."
else
  echo "WSL default gateway not found; dependency downloads will use direct network."
fi
'@.Replace("__PORT__", [string]$LocalProxyPort)
}

function Get-GitCommandScript {
    param([string]$GitArgs)

    if ($LocalProxyPort -le 0) {
        return "git $GitArgs"
    }

    return @'
anima_wsl_host=$(ip route | awk '/^default/ {print $3; exit}')
if [ -n "$anima_wsl_host" ]; then
  anima_git_proxy="http://$anima_wsl_host:__PORT__"
  git -c http.proxy="$anima_git_proxy" -c https.proxy="$anima_git_proxy" __ARGS__
else
  git __ARGS__
fi
'@.Replace("__PORT__", [string]$LocalProxyPort).Replace("__ARGS__", $GitArgs)
}

function Invoke-WslGit {
    param(
        [string]$Label,
        [string]$GitArgs,
        [int]$TimeoutSeconds = 1800
    )

    Invoke-WslBash -Label $Label -Script (Get-GitCommandScript $GitArgs) -TimeoutSeconds $TimeoutSeconds | Out-Null
}

function Test-WslExe {
    return $null -ne (Get-Command "wsl.exe" -ErrorAction SilentlyContinue)
}

function Test-Wsl2Available {
    if (-not (Test-WslExe)) {
        return $false
    }

    try {
        $result = Invoke-Native -Label "检测 WSL 发行版" -Command @("wsl.exe", "-l", "-v") -TimeoutSeconds 30 -NativeOutputEncoding ([System.Text.Encoding]::Unicode) -Optional
    }
    catch {
        return $false
    }

    return (($result.StdOut + "`n" + $result.StdErr) -match "\b2\b")
}

function Get-WslQuietList {
    try {
        $result = Invoke-Native -Label "读取 WSL 发行版列表" -Command @("wsl.exe", "--list", "--quiet") -TimeoutSeconds 30 -NativeOutputEncoding ([System.Text.Encoding]::Unicode) -Optional
        return (($result.StdOut + "`n" + $result.StdErr) -replace "`0", "")
    }
    catch {
        return ""
    }
}

function Test-UbuntuInstalled {
    $expected = $Distro.Trim().ToLowerInvariant()
    return (Get-WslQuietList).Split("`n") |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { $_ -eq $expected } |
        Select-Object -First 1
}

function Test-UbuntuRootReady {
    try {
        $result = Invoke-Native -Label "探测 Ubuntu root shell" -Command @(
            "wsl.exe",
            "-d",
            $Distro,
            "-u",
            "root",
            "--",
            "bash",
            "-lc",
            "printf WSL_ROOT_READY"
        ) -TimeoutSeconds 8 -Optional
        return (($result.StdOut + $result.StdErr) -like "*WSL_ROOT_READY*")
    }
    catch {
        return $false
    }
}

function Install-WindowsWslFeature {
    if (Test-Wsl2Available) {
        Write-Host "已检测到可用 WSL2 发行版，跳过 Windows WSL 安装器。"
        return
    }

    Write-Host "WSL 安装器使用 Windows 系统源；镜像源会用于后续 WSL 内依赖安装。"
    Write-Host "安装 WSL 系统功能可能需要管理员权限；若失败请用管理员身份重新运行本软件后再试。"

    try {
        Invoke-Native -Label "安装 Windows WSL 功能" -Command @("wsl.exe", "--install", "--no-distribution") -TimeoutSeconds 1800 | Out-Null
    }
    catch {
        $text = $_.Exception.Message
        if ($text -notmatch "--install: unrecognized option: no-distribution") {
            throw
        }

        Write-Host "当前 Windows WSL 安装器不支持 --no-distribution，改用 Windows 可选功能兼容安装方式。"
        Invoke-Native -Label "启用 Windows Subsystem for Linux 可选功能" -Command @(
            "dism.exe",
            "/online",
            "/enable-feature",
            "/featurename:Microsoft-Windows-Subsystem-Linux",
            "/all",
            "/norestart"
        ) -TimeoutSeconds 1800 | Out-Null
        Invoke-Native -Label "启用 Virtual Machine Platform 可选功能" -Command @(
            "dism.exe",
            "/online",
            "/enable-feature",
            "/featurename:VirtualMachinePlatform",
            "/all",
            "/norestart"
        ) -TimeoutSeconds 1800 | Out-Null
    }

    Write-Host "WSL 系统功能安装命令已执行。若 Windows 提示需要重启，请重启后再安装 Ubuntu 子系统。"
}

function Download-UbuntuRootfs {
    $downloadDir = Join-Path $ManagedRoot "downloads"
    $rootfsPath = Join-Path $downloadDir "ubuntu-noble-wsl-amd64.rootfs.tar.gz"
    $minimumBytes = 100MB
    New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

    if ((Test-Path -LiteralPath $rootfsPath) -and ((Get-Item -LiteralPath $rootfsPath).Length -ge $minimumBytes)) {
        Write-Host "Using cached Ubuntu WSL rootfs: $rootfsPath"
        return $rootfsPath
    }

    $lastError = $null
    foreach ($url in $UbuntuRootfsUrls) {
        Write-Host "Downloading Ubuntu WSL rootfs from $url"
        Remove-Item -LiteralPath $rootfsPath -Force -ErrorAction SilentlyContinue
        try {
            if (Get-Command "curl.exe" -ErrorAction SilentlyContinue) {
                Invoke-Native -Label "下载 Ubuntu WSL rootfs" -Command @(
                    "curl.exe",
                    "-L",
                    "--fail",
                    "--retry",
                    "3",
                    "--connect-timeout",
                    "20",
                    "-o",
                    $rootfsPath,
                    $url
                ) -TimeoutSeconds 1800 | Out-Null
            }
            else {
                Invoke-WebRequest -Uri $url -OutFile $rootfsPath -UseBasicParsing
            }

            if ((Test-Path -LiteralPath $rootfsPath) -and ((Get-Item -LiteralPath $rootfsPath).Length -ge $minimumBytes)) {
                return $rootfsPath
            }

            $lastError = "Downloaded file is too small"
        }
        catch {
            $lastError = $_.Exception.Message
        }
    }

    throw "下载 Ubuntu WSL rootfs 失败：$lastError"
}

function Import-UbuntuRootfs {
    $rootfsPath = Download-UbuntuRootfs
    $installDir = Join-Path $ManagedRoot "wsl\Ubuntu"
    if (Test-Path -LiteralPath $installDir) {
        Remove-Item -LiteralPath $installDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    Invoke-Native -Label "导入 Ubuntu WSL rootfs" -Command @(
        "wsl.exe",
        "--import",
        $Distro,
        $installDir,
        $rootfsPath,
        "--version",
        "2"
    ) -TimeoutSeconds 1800 | Out-Null

    if (-not (Test-UbuntuInstalled)) {
        throw "Ubuntu rootfs 已导入命令执行完成，但 wsl.exe --list 未检测到 Ubuntu。"
    }
}

function Stop-UbuntuDistribution {
    Invoke-OptionalNative -Label "关闭 WSL 服务以释放 Ubuntu 文件锁" -Command @("wsl.exe", "--shutdown") -TimeoutSeconds 20
}

function Unregister-UbuntuDistribution {
    Invoke-Native -Label "注销现有 Ubuntu WSL 发行版" -Command @("wsl.exe", "--unregister", $Distro) -TimeoutSeconds 60 | Out-Null
}

function Wait-UbuntuRemoved {
    Write-Step "等待 Ubuntu WSL 发行版完成注销"
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-UbuntuInstalled)) {
            Write-Host "Ubuntu WSL 发行版已注销。"
            return
        }
        Start-Sleep -Seconds 2
    }

    throw "注销 Ubuntu WSL 发行版后仍能检测到 Ubuntu。请关闭所有 Ubuntu/WSL 终端，在 Windows 终端执行 wsl.exe --shutdown 后再重试。"
}

function Ensure-UbuntuRootInitialized {
    Write-Step "检查 Ubuntu root 非交互启动状态"
    if (Test-UbuntuRootReady) {
        Write-Host "Ubuntu 子系统已完成初始化。"
        return
    }

    if (-not (Get-Command $UbuntuLauncher -ErrorAction SilentlyContinue)) {
        throw "未找到 ubuntu.exe 启动器，无法自动初始化 Ubuntu。请确认 Ubuntu 发行版已正确安装。"
    }

    try {
        Invoke-Native -Label "自动初始化 Ubuntu 子系统（root，无需用户名密码）" -Command @($UbuntuLauncher, "install", "--root") -TimeoutSeconds 600 | Out-Null
    }
    catch {
        if ($_.Exception.Message -notmatch "already installed|already exists|ERROR_ALREADY_EXISTS|已经安装|已安装") {
            throw
        }

        Write-Host "Ubuntu 启动器提示发行版已安装，继续校验初始化状态。"
    }

    if (Test-UbuntuRootReady) {
        Write-Host "Ubuntu 子系统 root 初始化完成。"
        return
    }

    throw "Ubuntu 子系统已安装，但 root 初始化尚未完成。若刚安装或更新 WSL，请重启 Windows 后再重试安装 Ubuntu 子系统。"
}

function Install-UbuntuDistribution {
    param([bool]$Reinstall = $false)

    Write-Host "Ubuntu 子系统安装器使用 Windows 系统源；镜像源会用于后续 WSL 内 diffusion-pipe 部署。"
    if (Test-UbuntuInstalled) {
        if ($Reinstall) {
            Write-Host "检测到 Ubuntu WSL 发行版已存在，将重新安装。"
            Write-Host "注意：重新安装会删除 Ubuntu 子系统内的全部数据。"
            Stop-UbuntuDistribution
            Unregister-UbuntuDistribution
            Wait-UbuntuRemoved
        }
        else {
            Write-Host "Ubuntu WSL 发行版已存在，跳过重复安装。"
            Ensure-UbuntuRootInitialized
            return
        }
    }

    Invoke-Native -Label "更新 Windows WSL 组件" -Command @("wsl.exe", "--update", "--web-download") -TimeoutSeconds 900 | Out-Null
    Invoke-Native -Label "列出可安装的 WSL 发行版" -Command @("wsl.exe", "--list", "--online") -TimeoutSeconds 120 -NativeOutputEncoding ([System.Text.Encoding]::Unicode) | Out-Null

    $installCommand = @("wsl.exe", "--install", "--distribution", $Distro, "--no-launch")
    try {
        Invoke-Native -Label "安装 Ubuntu WSL 发行版" -Command $installCommand -TimeoutSeconds 2700 | Out-Null
    }
    catch {
        $message = $_.Exception.Message
        if ($message -match "ERROR_ALREADY_EXISTS|已存在具有所提供名称的分发") {
            Write-Host "Ubuntu WSL 发行版已存在，跳过重复安装。"
        }
        elseif (Test-UbuntuInstalled) {
            Write-Host "Ubuntu 安装器返回非零状态，但已检测到 Ubuntu 发行版，继续初始化。"
        }
        elseif ($message -match "--install: unrecognized option: no-launch") {
            Write-Host "WSL installer does not support --no-launch; importing Ubuntu rootfs instead."
            Import-UbuntuRootfs
        }
        else {
            Write-Host "Ubuntu 安装器返回非零状态，先关闭 WSL 服务释放 HCS/虚拟机状态，再改用 --web-download 在线源重试。"
            Stop-UbuntuDistribution
            try {
                Invoke-Native -Label "安装 Ubuntu WSL 发行版（在线源兜底）" -Command ($installCommand + "--web-download") -TimeoutSeconds 2700 | Out-Null
            }
            catch {
                if (Test-UbuntuInstalled) {
                    Write-Host "Ubuntu 安装器返回非零状态，但已检测到 Ubuntu 发行版，继续初始化。"
                }
                else {
                    Write-Host "Ubuntu installer fallback failed; importing Ubuntu rootfs instead."
                    Import-UbuntuRootfs
                }
            }
        }
    }

    Ensure-UbuntuRootInitialized
    Write-Host "Ubuntu 子系统已就绪。若 Windows 提示需要重启，请重启后再安装 diffusion-pipe WSL 环境。"
}

function Install-WslPrerequisites {
    $mirrorSetup = ""
    if ($MirrorProfile.Apt) {
        $aptUrl = $MirrorProfile.Apt.TrimEnd("/") + "/"
        $aptReplacement = $aptUrl.Replace("#", "\#").Replace("&", "\&")
        $mirrorSetup = @"
if [ -f /etc/apt/sources.list.d/ubuntu.sources ]; then
  cp /etc/apt/sources.list.d/ubuntu.sources /etc/apt/sources.list.d/ubuntu.sources.cyantools.bak 2>/dev/null || true
  sed -i -E 's#https?://(archive|security)\.ubuntu\.com/ubuntu/?#$aptReplacement#g' /etc/apt/sources.list.d/ubuntu.sources
elif [ -f /etc/apt/sources.list ]; then
  cp /etc/apt/sources.list /etc/apt/sources.list.cyantools.bak 2>/dev/null || true
  sed -i -E 's#https?://(archive|security)\.ubuntu\.com/ubuntu/?#$aptReplacement#g' /etc/apt/sources.list
fi
"@
    }

    $script = @"
set -e
export DEBIAN_FRONTEND=noninteractive
if ! command -v apt-get >/dev/null 2>&1; then
  echo 'apt-get not found; skip WSL package bootstrap.'
  exit 0
fi
if [ "`$(id -u)" -eq 0 ]; then SUDO=''
elif command -v sudo >/dev/null 2>&1; then SUDO='sudo'
else echo 'Need root or sudo to install WSL prerequisites.' >&2; exit 126; fi
$mirrorSetup
`$SUDO apt-get update
`$SUDO apt-get install -y ca-certificates curl bzip2 git build-essential
"@
    Invoke-WslBash -Label "安装 WSL 基础依赖" -Script $script | Out-Null
}

function Remove-WslRepo {
    Invoke-WslBash -Label "清理 WSL diffusion-pipe 仓库目录" -Script "rm -rf $RepoDir" | Out-Null
}

function Remove-CondaEnv {
    $conda = Get-CondaSourceSnippet
    $script = "if $conda && command -v conda >/dev/null 2>&1; then conda env remove -n $CondaEnvName -y || true; else echo 'Conda not found; skip old diffusion-pipe environment removal.'; fi"
    Invoke-WslBash -Label "移除旧的 diffusion-pipe WSL conda 环境" -Script $script | Out-Null
}

function Invoke-WithRetries {
    param(
        [string]$Label,
        [scriptblock]$Operation,
        [scriptblock]$BeforeRetry = $null
    )

    $maxAttempts = 3
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            & $Operation $attempt
            return
        }
        catch {
            if ($attempt -ge $maxAttempts) {
                throw
            }

            Write-Host "$Label 中断，开始第 $($attempt + 1) 次尝试。$($_.Exception.Message)"
            if ($BeforeRetry) {
                & $BeforeRetry
            }
            Start-Sleep -Seconds ([Math]::Min(5, $attempt * 2))
        }
    }
}

function Clone-Or-UpdateRepo {
    param([bool]$UpdateOnly = $false)

    Invoke-WslBash -Label "准备 WSL diffusion-pipe 目录" -Script "mkdir -p `$HOME" | Out-Null
    $repoExists = $false
    try {
        Invoke-WslBash -Label "检查 diffusion-pipe 仓库" -Script "test -d $RepoDir/.git" -TimeoutSeconds 30 | Out-Null
        $repoExists = $true
    }
    catch {
        $repoExists = $false
    }

    if ($UpdateOnly -and -not $repoExists) {
        throw "diffusion-pipe 尚未安装，无法更新。请先点击安装。"
    }

    if ($repoExists) {
        Invoke-WithRetries -Label "更新 diffusion-pipe 仓库" -Operation {
            param($attempt)
            $label = if ($attempt -eq 1) { "更新 diffusion-pipe 仓库" } else { "更新 diffusion-pipe 仓库（第 $attempt 次）" }
            Invoke-WslGit -Label $label -GitArgs "-C $RepoDir -c http.version=HTTP/1.1 -c http.lowSpeedLimit=0 -c core.compression=0 pull --ff-only"
        }
    }
    else {
        Invoke-WithRetries -Label "克隆 diffusion-pipe 仓库" -BeforeRetry { Remove-WslRepo } -Operation {
            param($attempt)
            Remove-WslRepo
            $label = if ($attempt -eq 1) { "克隆 diffusion-pipe 仓库" } else { "克隆 diffusion-pipe 仓库（第 $attempt 次）" }
            Invoke-WslGit -Label $label -GitArgs "-c http.version=HTTP/1.1 -c http.lowSpeedLimit=0 -c core.compression=0 clone --filter=blob:none --single-branch --branch $RepoRef $RepoUrl $RepoDir"
        }
    }

    foreach ($submodule in $RequiredSubmodules) {
        Invoke-WithRetries -Label "同步 diffusion-pipe 子模块 $submodule" -BeforeRetry {
            Invoke-WslBash -Label "清理 diffusion-pipe 子模块 $submodule" -Script "rm -rf $RepoDir/$submodule $RepoDir/.git/modules/$submodule" | Out-Null
        } -Operation {
            param($attempt)
            $label = if ($attempt -eq 1) { "同步 diffusion-pipe 子模块 $submodule" } else { "同步 diffusion-pipe 子模块 $submodule（第 $attempt 次）" }
            Invoke-WslGit -Label $label -GitArgs "-C $RepoDir -c http.version=HTTP/1.1 -c http.lowSpeedLimit=0 -c core.compression=0 submodule update --init --depth 1 --filter=blob:none --recommend-shallow --jobs 1 $submodule"
        }
    }
}

function Get-MinicondaUrls {
    $urls = New-Object System.Collections.Generic.List[string]
    if ($MirrorProfile.Conda) {
        $urls.Add($MirrorProfile.Conda.TrimEnd("/") + "/miniconda/Miniconda3-latest-Linux-x86_64.sh")
    }
    if ($Mirror -ne "tsinghua") {
        $urls.Add("https://mirrors.tuna.tsinghua.edu.cn/anaconda/miniconda/Miniconda3-latest-Linux-x86_64.sh")
    }
    $urls.Add("https://repo.anaconda.com/miniconda/Miniconda3-latest-Linux-x86_64.sh")
    return $urls | Select-Object -Unique
}

function Ensure-Miniconda {
    $conda = Get-CondaSourceSnippet
    $proxy = Get-ProxyEnvScript
    $downloadAttempts = (Get-MinicondaUrls | ForEach-Object {
        "( echo 'Downloading Miniconda from $_' && curl -L --fail --retry 3 --connect-timeout 20 -o `"`$HOME/miniconda-installer.sh`" '$_' )"
    }) -join " || "

    $script = @"
set -e
$proxy
if $conda >/dev/null 2>&1; then
  echo 'WSL Miniconda already available.'
  conda --version
  exit 0
fi
if ! command -v curl >/dev/null 2>&1; then
  if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y ca-certificates curl bzip2
  else
    echo 'WSL lacks curl and apt-get, cannot download Miniconda.' >&2
    exit 127
  fi
fi
rm -f "`$HOME/miniconda-installer.sh"
if ! ( $downloadAttempts ); then
  echo 'Failed to download Miniconda installer from all mirrors.' >&2
  exit 22
fi
bash "`$HOME/miniconda-installer.sh" -b -p "`$HOME/miniconda3"
rm -f "`$HOME/miniconda-installer.sh"
. "`$HOME/miniconda3/etc/profile.d/conda.sh"
export CONDA_PLUGINS_AUTO_ACCEPT_TOS=yes
conda config --set auto_activate_base false || true
conda config --set plugins.auto_accept_tos yes || true
conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/main || true
conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/r || true
conda --version
"@
    Invoke-WslBash -Label "检查/安装 WSL Miniconda" -Script $script | Out-Null
}

function Get-CondaDefaultChannels {
    if (-not $MirrorProfile.Conda) {
        return ""
    }

    $base = $MirrorProfile.Conda.TrimEnd("/")
    return "--override-channels -c $base/pkgs/main -c $base/pkgs/r -c $base/pkgs/msys2"
}

function Get-CondaNvidiaChannels {
    if (-not $MirrorProfile.Conda) {
        return "-c nvidia"
    }

    $base = $MirrorProfile.Conda.TrimEnd("/")
    return "--override-channels -c $base/cloud/nvidia"
}

function Get-PipIndexArgs {
    if (-not $MirrorProfile.Pip) {
        return ""
    }

    return "--index-url $($MirrorProfile.Pip)"
}

function Get-PipRetryFunction {
    return @'
anima_pip_install() {
  local anima_pip_attempt
  for anima_pip_attempt in 1 2 3; do
    python -m pip install --retries 5 --timeout 60 "$@" && return 0
    if [ "$anima_pip_attempt" -lt 3 ]; then
      echo "pip install failed, retrying..." >&2
      sleep 5
    fi
  done
  return 1
}
'@
}

function Install-Or-UpdateEnvironment {
    param([bool]$UpdateOnly = $false)

    Ensure-Miniconda
    $conda = Get-CondaSourceSnippet
    $proxy = Get-ProxyEnvScript
    $condaCreateChannels = Get-CondaDefaultChannels
    $condaNvidiaChannels = Get-CondaNvidiaChannels
    $pipArgs = Get-PipIndexArgs
    $pipInstall = ("anima_pip_install " + $pipArgs).Trim()
    $pipRetry = Get-PipRetryFunction

    $script = @"
set -e
$proxy
$conda
export CONDA_PLUGINS_AUTO_ACCEPT_TOS=yes
conda create -n $CondaEnvName python=3.12 pysocks -y $condaCreateChannels || true
conda install -n $CondaEnvName pysocks -y $condaCreateChannels || true
conda activate $CondaEnvName
mkdir -p "`$HOME/.cache/anima-pip" "`$HOME/tmp"
export TMPDIR="`$HOME/tmp" PIP_CACHE_DIR="`$HOME/.cache/anima-pip"
$pipRetry
python -m pip install --index-url https://download.pytorch.org/whl/$CudaTag torch==$TorchVersion+$CudaTag torchvision==$TorchVisionVersion+$CudaTag torchaudio==$TorchAudioVersion+$CudaTag
$pipInstall scipy
$pipInstall -r $RepoDir/requirements.txt
conda install $condaNvidiaChannels cuda-nvcc=12.8 -y
export CUDA_HOME="`$CONDA_PREFIX"
export PATH="`$CUDA_HOME/bin:`$PATH"
export CC=/usr/bin/gcc CXX=/usr/bin/g++ CUDAHOSTCXX=/usr/bin/g++
MAX_JOBS=2 python -m pip install $pipArgs flash-attn==$FlashAttnVersion --no-build-isolation
"@
    $label = if ($UpdateOnly) { "更新 diffusion-pipe WSL 依赖" } else { "创建 diffusion-pipe WSL conda 环境" }
    Invoke-WslBash -Label $label -Script $script -TimeoutSeconds 0 | Out-Null
}

function Apply-AnimaPatch {
    $conda = Get-CondaSourceSnippet
    $python = @'
from pathlib import Path
import re

repo = Path.home() / "diffusion-pipe"

cosmos_path = repo / "models" / "cosmos_predict2.py"
if cosmos_path.exists():
    text = cosmos_path.read_text(encoding="utf-8")
    patched = text
    keep_pattern = r"KEEP_IN_HIGH_PRECISION\s*=\s*\[[^\]]*\]"
    keep_line = "KEEP_IN_HIGH_PRECISION = ['x_embedder', 't_embedder', 't_embedding_norm', 'final_layer', 'llm_adapter']"
    patched = re.sub(keep_pattern, keep_line, patched, count=1)
    if "def _tokenize(tokenizer, prompts, max_length=512):" not in patched:
        patched = patched.replace(
            "def _tokenize(tokenizer, prompts):",
            "def _tokenize(tokenizer, prompts, max_length=512):",
        )
        patched = patched.replace("max_length=512,", "max_length=max_length,", 1)
    sequence_line = "        self.max_sequence_length = self.model_config.get('max_sequence_length', 512)\n"
    if sequence_line not in patched:
        patched = patched.replace(
            "        self.cache_text_embeddings = self.model_config.get('cache_text_embeddings', True)\n",
            "        self.cache_text_embeddings = self.model_config.get('cache_text_embeddings', True)\n" + sequence_line,
            1,
        )
    patched = patched.replace(
        "_tokenize(self.tokenizer, captions)",
        "_tokenize(self.tokenizer, captions, self.max_sequence_length)",
    )
    patched = patched.replace(
        "_tokenize(self.t5_tokenizer, captions)",
        "_tokenize(self.t5_tokenizer, captions, self.max_sequence_length)",
    )
    if patched != text:
        cosmos_path.write_text(patched, encoding="utf-8")

offloading_path = repo / "utils" / "offloading.py"
if offloading_path.exists():
    text = offloading_path.read_text(encoding="utf-8")
    patched = text.replace(
        'module_to_cpu.weight.data = cuda_data_view.data.to("cpu", non_blocking=True)',
        'module_to_cpu.weight.data = cuda_data_view.data.to("cpu")',
        1,
    )
    helper = '''

def non_weight_and_trainable_to_device(layer: nn.Module, device: torch.device):
    weight_ids = set()
    for module in layer.modules():
        if hasattr(module, "weight") and module.weight is not None:
            weight_ids.add(id(module.weight))

    for param in layer.parameters():
        if param.requires_grad or id(param) not in weight_ids:
            param.data = param.data.to(device, non_blocking=True)

    for buffer in layer.buffers():
        buffer.data = buffer.data.to(device, non_blocking=True)
'''
    if "def non_weight_and_trainable_to_device" not in patched:
        patched = patched.replace("\n\nclass Offloader:", helper + "\n\nclass Offloader:", 1)
    patched = patched.replace(
        "        for b in self.blocks[self.num_blocks - self.blocks_to_swap :]:\n"
        "            b.to(self.device)\n"
        "            weights_to_device(b, torch.device('cpu'))  # make sure weights are on cpu\n",
        "        for b in self.blocks[self.num_blocks - self.blocks_to_swap :]:\n"
        "            weights_to_device(b, torch.device('cpu'))  # make sure weights are on cpu\n"
        "            non_weight_and_trainable_to_device(b, self.device)\n",
        1,
    )
    if patched != text:
        offloading_path.write_text(patched, encoding="utf-8")
print("Anima compatibility patch ready")
'@

    $script = @"
set -e
$conda
conda activate $CondaEnvName
python - <<'PY'
$python
PY
"@
    Invoke-WslBash -Label "应用 Anima 1024 训练兼容补丁" -Script $script | Out-Null
}

function Apply-SmokePatch {
    $conda = Get-CondaSourceSnippet
    $python = @'
from pathlib import Path

repo = Path.cwd()
module = r"""
from pathlib import Path

import numpy as np
import torch
from torch import nn
import torch.nn.functional as F
from PIL import Image, ImageOps


class SmokePreprocessMediaFile:
    def __init__(self, width: int = 16, height: int = 16):
        self.width = width
        self.height = height

    def __call__(self, spec, mask_filepath, size_bucket=None):
        image_path = str(spec[1])
        if size_bucket is not None:
            width, height = int(size_bucket[0]), int(size_bucket[1])
        else:
            width, height = self.width, self.height
        image = Image.open(image_path).convert("RGB")
        image = ImageOps.fit(image, (width, height))
        array = np.asarray(image, dtype=np.float32) / 127.5 - 1.0
        tensor = torch.from_numpy(array).permute(2, 0, 1).contiguous()
        return [(tensor, None)]


class SmokeVAE(nn.Module):
    def __init__(self):
        super().__init__()
        self.anchor = nn.Parameter(torch.zeros(()))


class SmokeLayer(nn.Module):
    def __init__(self, width):
        super().__init__()
        self.proj = nn.Linear(width, width)
        for name, param in self.proj.named_parameters():
            param.original_name = f"proj.{name}"

    def forward(self, inputs):
        if isinstance(inputs, tuple):
            x = inputs[0]
        else:
            x = inputs
        return self.proj(x.float())


class SmokePipeline:
    name = "anima_smoke"
    framerate = None
    pixels_round_to_multiple = 1

    def __init__(self, config):
        self.config = config
        self.model_config = config["model"]
        self.width = int(self.model_config.get("width", 3))
        self.vae = SmokeVAE()
        self.layer = SmokeLayer(self.width)

    def load_diffusion_model(self):
        return None

    def get_vae(self):
        return self.vae

    def get_text_encoders(self):
        return []

    def get_preprocess_media_file_fn(self):
        return SmokePreprocessMediaFile()

    def get_call_vae_fn(self, vae):
        def fn(images):
            images = images.to("cuda").float()
            latents = images.mean(dim=tuple(range(2, images.ndim)))
            if latents.shape[1] < self.width:
                latents = F.pad(latents, (0, self.width - latents.shape[1]))
            latents = latents[:, : self.width]
            return {"latents": latents + vae.anchor.to(latents.device)}
        return fn

    def get_call_text_encoder_fn(self, text_encoder):
        raise NotImplementedError("SmokePipeline has no text encoder")

    def model_specific_dataset_config_validation(self, dataset_config):
        return None

    def configure_adapter(self, adapter_config):
        return None

    def prepare_inputs(self, inputs, timestep_quantile=None):
        latents = inputs["latents"].to("cuda").float()
        target = latents * 0.5
        mask = inputs.get("mask")
        if mask is None:
            mask = torch.tensor([], device=latents.device)
        else:
            mask = mask.to(latents.device)
        return (latents,), (target, mask)

    def to_layers(self):
        return [self.layer]

    def get_param_groups(self, parameters):
        return [{"params": parameters}]

    def get_loss_fn(self):
        def loss_fn(output, label):
            target, mask = label
            output = output.to(torch.float32)
            target = target.to(output.device, torch.float32)
            loss = F.mse_loss(output, target, reduction="none")
            if mask.numel() > 0:
                loss *= mask.to(output.device, torch.float32)
            return loss.mean()
        return loss_fn

    def prepare_block_swap_training(self):
        return None

    def prepare_block_swap_inference(self, disable_block_swap=False):
        return None

    def save_model(self, save_dir, state_dict):
        save_dir = Path(save_dir)
        save_dir.mkdir(parents=True, exist_ok=True)
        torch.save(state_dict, save_dir / "smoke_state.pt")
"""

(repo / "models" / "anima_smoke.py").write_text(module, encoding="utf-8")
train_path = repo / "train.py"
text = train_path.read_text(encoding="utf-8")
needle = "    if model_type == 'flux':\n"
branch = (
    "    if model_type == 'anima_smoke':\n"
    "        from models import anima_smoke\n"
    "        model = anima_smoke.SmokePipeline(config)\n"
    "    elif model_type == 'flux':\n"
)
if branch not in text:
    if needle not in text:
        raise RuntimeError("Could not locate diffusion-pipe model branch insertion point")
    text = text.replace(needle, branch, 1)
    train_path.write_text(text, encoding="utf-8")
print("diffusion-pipe smoke training patch ready")
'@

    $script = @"
set -e
cd $RepoDir
$conda
conda activate $CondaEnvName
export CONDA_PREFIX="`$\{CONDA_PREFIX:-`$HOME/miniconda3/envs/$CondaEnvName\}"
"`$CONDA_PREFIX/bin/python" - <<'PY'
$python
PY
"@
    Invoke-WslBash -Label "应用 diffusion-pipe 1 step smoke 训练补丁" -Script $script | Out-Null
}

function Run-SmokeTraining {
    $conda = Get-CondaSourceSnippet
    $filesPython = @'
from pathlib import Path
from PIL import Image

root = Path.home() / ".cache" / "anima-diffusion-pipe-smoke"
data_dir = root / "data"
output_dir = root / "output"
data_dir.mkdir(parents=True, exist_ok=True)
output_dir.mkdir(parents=True, exist_ok=True)

Image.new("RGB", (16, 16), (128, 64, 192)).save(data_dir / "sample.png")
(data_dir / "sample.txt").write_text("smoke sample\n", encoding="utf-8")

(root / "dataset.toml").write_text(
    f"""resolutions = [16]
enable_ar_bucket = false

[[directory]]
path = "{data_dir.as_posix()}"
num_repeats = 2
shuffle_metadata = false
""",
    encoding="utf-8",
)

(root / "config.toml").write_text(
    f"""pipeline_stages = 1
output_dir = "{output_dir.as_posix()}"
dataset = "{(root / 'dataset.toml').as_posix()}"
epochs = 1
max_steps = 1
micro_batch_size_per_gpu = 1
gradient_accumulation_steps = 1
save_every_n_steps = 1000
logging_steps = 1
eval_before_first_step = false

[model]
type = "anima_smoke"
dtype = "float32"
width = 3

[optimizer]
type = "sgd"
lr = 0.01
""",
    encoding="utf-8",
)
print(root / "config.toml")
'@

    $script = @"
set -e
cd $RepoDir
$conda
conda activate $CondaEnvName
export CONDA_PREFIX="`$\{CONDA_PREFIX:-`$HOME/miniconda3/envs/$CondaEnvName\}"
export CUDA_HOME="`$CONDA_PREFIX"
export PATH="`$CUDA_HOME/bin:`$PATH"
export TMPDIR="`$HOME/tmp"
export PIP_CACHE_DIR="`$HOME/.cache/anima-pip"
mkdir -p "`$TMPDIR" "`$PIP_CACHE_DIR"
export PYTORCH_ALLOC_CONF=expandable_segments:True
export NCCL_P2P_DISABLE=1
export NCCL_IB_DISABLE=1
SMOKE_ROOT="`$HOME/.cache/anima-diffusion-pipe-smoke"
rm -rf "`$SMOKE_ROOT"
mkdir -p "`$SMOKE_ROOT/data" "`$SMOKE_ROOT/output"
python - <<'PY'
$filesPython
PY
"`$CONDA_PREFIX/bin/deepspeed" --num_gpus=1 train.py --deepspeed --config "`$SMOKE_ROOT/config.toml" --trust_cache --i_know_what_i_am_doing
test -n "`$(find "`$SMOKE_ROOT/output" -name smoke_state.pt -print -quit)"
echo "diffusion-pipe 1 step smoke training finished."
"@
    Invoke-WslBash -Label "运行 diffusion-pipe 1 step smoke 训练" -Script $script | Out-Null
}

function Validate-DiffusionPipe {
    if (-not (Test-Wsl2Available)) {
        throw "diffusion-pipe WSL2 环境不可用。请先安装 WSL2 和 NVIDIA WSL 驱动。"
    }
    Ensure-UbuntuRootInitialized

    $conda = Get-CondaSourceSnippet
    $script = @"
set -e
command -v git
test -f $RepoDir/train.py
test -f $RepoDir/requirements.txt
$conda
conda activate $CondaEnvName
export CUDA_HOME="`$CONDA_PREFIX" PATH="`$CUDA_HOME/bin:`$PATH"
python --version
python - <<'PY'
import deepspeed
import flash_attn
import torch
import torchaudio
print('torch', torch.__version__)
print('deepspeed', deepspeed.__version__)
print('flash_attn', flash_attn.__version__)
print('torchaudio', torchaudio.__version__)
PY
echo "diffusion-pipe validation flow finished."
"@
    Invoke-WslBash -Label "校验 diffusion-pipe" -Script $script | Out-Null
    Apply-SmokePatch
    Run-SmokeTraining
    Write-Host "diffusion-pipe WSL 环境校验完成，1 step 训练 smoke 已通过。"
}

function Save-DiffusionPipeManifest {
    $revision = ""
    try {
        $result = Invoke-WslGit -Label "记录 diffusion-pipe 当前版本" -GitArgs "-C $RepoDir rev-parse HEAD"
        $revision = (($result.StdOut + "`n" + $result.StdErr).Trim())
    }
    catch {
        $revision = "unknown"
    }

    $manifestDir = Join-Path $ManagedRoot "state\manifests"
    New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
    $manifestPath = Join-Path $manifestDir "diffusion-pipe.json"
    [ordered]@{
        backend_id = "diffusion-pipe"
        repo_url = $RepoUrl
        requested_ref = $RepoRef
        resolved_revision = $revision
        runtime_target = "wsl2"
        interpreter_path = "wsl.exe conda:$CondaEnvName"
        install_root = "~/diffusion-pipe"
        created_at = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Manifest saved: $manifestPath"
}

function Install-DiffusionPipe {
    param(
        [bool]$Reinstall = $false,
        [bool]$UpdateOnly = $false
    )

    Write-Host "安装镜像源：$($MirrorProfile.Label)"
    if ($LocalProxyPort -gt 0) {
        Write-Host "WSL 本地代理端口：$LocalProxyPort（Git、pip、conda 将动态使用 Windows 主机网关）。"
    }

    if (-not (Test-Wsl2Available)) {
        throw "diffusion-pipe WSL2 环境不可用。请先安装 WSL2 和 NVIDIA WSL 驱动。"
    }

    if (-not (Test-UbuntuInstalled)) {
        throw "未检测到 Ubuntu 子系统。请先点击'安装 Ubuntu 子系统'。"
    }

    Ensure-UbuntuRootInitialized
    Install-WslPrerequisites

    if ($Reinstall) {
        Remove-WslRepo
        Remove-CondaEnv
    }

    Clone-Or-UpdateRepo -UpdateOnly:$UpdateOnly
    if ($UpdateOnly) {
        Install-Or-UpdateEnvironment -UpdateOnly:$true
    }
    else {
        Install-Or-UpdateEnvironment
    }

    Apply-AnimaPatch
    Apply-SmokePatch
    Run-SmokeTraining
    Save-DiffusionPipeManifest
    Write-Host "diffusion-pipe / WSL2 部署完成，1 step 训练 smoke 已通过。"
}

function Show-Status {
    Write-Host "托管目录：$ManagedRoot"
    if (-not (Test-WslExe)) {
        Write-Host "未找到 wsl.exe。"
        return
    }

    Invoke-Native -Label "WSL 发行版状态" -Command @("wsl.exe", "-l", "-v") -TimeoutSeconds 30 -NativeOutputEncoding ([System.Text.Encoding]::Unicode) -Optional | Out-Null

    if (Test-UbuntuInstalled) {
        Write-Host "Ubuntu 子系统：已安装"
        if (Test-UbuntuRootReady) {
            Write-Host "Ubuntu root shell：可用"
        }
        else {
            Write-Host "Ubuntu root shell：尚未初始化或不可用"
        }
    }
    else {
        Write-Host "Ubuntu 子系统：未检测到"
    }

    try {
        Invoke-WslBash -Label "diffusion-pipe 仓库状态" -Script "test -f $RepoDir/train.py && echo 'repo: ok' || echo 'repo: missing'" -TimeoutSeconds 30 | Out-Null
    }
    catch {
        Write-Host "diffusion-pipe 仓库状态读取失败：$($_.Exception.Message)"
    }

    try {
        Invoke-WslBash -Label "diffusion-pipe conda 环境状态" -Script "$(Get-CondaSourceSnippet) && conda run -n $CondaEnvName python --version" -TimeoutSeconds 30 | Out-Null
    }
    catch {
        Write-Host "diffusion-pipe conda 环境尚不可用。"
    }
}

if ($LibraryMode) {
    return
}

try {
    Write-Host "CyanTools diffusion-pipe / WSL2 deployment"
    Write-Host "Action: $Action"
    Write-Host "Mirror: $($MirrorProfile.Label)"

    switch ($Action) {
        "Check" {
            Show-Status
        }
        "InstallWsl" {
            Install-WindowsWslFeature
        }
        "InstallUbuntu" {
            Install-UbuntuDistribution
        }
        "Install" {
            Install-DiffusionPipe
        }
        "Update" {
            Install-DiffusionPipe -UpdateOnly:$true
        }
        "Reinstall" {
            Install-DiffusionPipe -Reinstall:$true
        }
        "Validate" {
            Validate-DiffusionPipe
        }
        "DeleteUbuntu" {
            Write-Host "注意：该操作会删除 Ubuntu 子系统内的全部数据。"
            if (Test-UbuntuInstalled) {
                Stop-UbuntuDistribution
                Unregister-UbuntuDistribution
                Wait-UbuntuRemoved
                Write-Host "Ubuntu 子系统已删除。"
            }
            else {
                Write-Host "未检测到 Ubuntu WSL 发行版，无需删除。"
            }
        }
    }

    Write-Host ""
    Write-Host "Action completed: $Action"
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
