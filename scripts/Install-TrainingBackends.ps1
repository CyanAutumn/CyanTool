param(
    [ValidateSet("wsl", "sd-scripts", "anima-fast-lora", "wsl-sd-scripts", "diffusion-pipe")]
    [string]$Backend = "diffusion-pipe",

    [ValidateSet("Check", "InstallWsl", "InstallUbuntu", "Install", "Update", "Reinstall", "Validate", "DeleteUbuntu")]
    [string]$Action = "Check",

    [ValidateSet("default", "tsinghua", "aliyun")]
    [string]$Mirror = "default",

    [int]$LocalProxyPort = 0,

    [string]$ManagedRoot = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

if ([string]::IsNullOrWhiteSpace($ManagedRoot)) {
    $ManagedRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "CyanTools\training-backends"
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

function Ensure-WindowsGit {
    $git = Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @(
        (Join-Path $env:ProgramFiles "Git\cmd\git.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Git\cmd\git.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")
    )
    if ($git) {
        return $git
    }

    $winget = Resolve-WindowsCommand -Name "winget.exe"
    if ($winget) {
        Invoke-Native -Label "安装 Windows Git" -Command @(
            $winget, "install", "-e", "--id", "Git.Git", "-s", "winget",
            "--accept-source-agreements", "--accept-package-agreements", "--silent"
        ) -TimeoutSeconds 1800 | Out-Null
        $git = Resolve-WindowsCommand -Name "git.exe" -FallbackPaths @(
            (Join-Path $env:ProgramFiles "Git\cmd\git.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Git\cmd\git.exe")
        )
    }
    if (-not $git) {
        throw "Windows 后端需要 Git。自动安装未成功，请先安装 Git for Windows 后重试。"
    }
    return $git
}

function Ensure-WindowsUv {
    $portableUv = Join-Path $ManagedRoot "tools\uv\uv.exe"
    $uv = Resolve-WindowsCommand -Name "uv.exe" -FallbackPaths @($portableUv)
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

function Sync-WindowsRepository {
    param(
        [string]$Git,
        [string]$RepoUrl,
        [string]$RepoPath,
        [bool]$Reinstall,
        [bool]$UpdateOnly
    )

    if ($Reinstall -and (Test-Path -LiteralPath $RepoPath)) {
        Write-Step "清理旧仓库：$RepoPath"
        Remove-Item -LiteralPath $RepoPath -Recurse -Force
    }
    if ($UpdateOnly -and -not (Test-Path -LiteralPath (Join-Path $RepoPath ".git"))) {
        throw "$Backend 尚未安装，无法更新。请先点击安装。"
    }

    if (Test-Path -LiteralPath (Join-Path $RepoPath ".git")) {
        Invoke-Native -Label "更新 $Backend 仓库" -Command @(
            $Git, "-C", $RepoPath, "-c", "http.version=HTTP/1.1", "pull", "--ff-only"
        ) -TimeoutSeconds 1800 | Out-Null
    }
    else {
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
}

function Get-WindowsPipMirrorArgs {
    if ($MirrorProfile.Pip) {
        return @("--index-url", [string]$MirrorProfile.Pip)
    }
    return @()
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

function Get-WindowsRevision {
    param([string]$Git, [string]$RepoPath)
    $result = Invoke-Native -Label "记录 $Backend 当前版本" -Command @($Git, "-C", $RepoPath, "rev-parse", "HEAD") -TimeoutSeconds 60
    return (($result.StdOut + "`n" + $result.StdErr).Trim())
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
    $git = Ensure-WindowsGit
    $uv = Ensure-WindowsUv
    if ($Reinstall -and (Test-Path -LiteralPath $SdScriptsWindowsVenv)) {
        Remove-Item -LiteralPath $SdScriptsWindowsVenv -Recurse -Force
    }
    Sync-WindowsRepository -Git $git -RepoUrl $SdScriptsRepoUrl -RepoPath $SdScriptsWindowsRepo -Reinstall $Reinstall -UpdateOnly $UpdateOnly

    Invoke-Native -Label "创建 sd-scripts Python 3.10 虚拟环境" -Command @(
        $uv, "venv", "--clear", "--python", "3.10", "--seed", $SdScriptsWindowsVenv
    ) -TimeoutSeconds 1800 | Out-Null
    $python = Join-Path $SdScriptsWindowsVenv "Scripts\python.exe"
    $pipMirror = @(Get-WindowsPipMirrorArgs)

    Invoke-Native -Label "安装 sd-scripts Python 依赖" -Command (@(
        $python, "-m", "pip", "install", "--retries", "5", "--timeout", "60"
    ) + $pipMirror + @("-r", (Join-Path $SdScriptsWindowsRepo "requirements.txt"))) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 CUDA 版 PyTorch / torchvision" -Command @(
        $python, "-m", "pip", "install", "--force-reinstall",
        "torch==2.10.0+cu128", "torchvision==0.25.0+cu128", "torchaudio==2.10.0+cu128",
        "--index-url", "https://download.pytorch.org/whl/cu128"
    ) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 xformers 加速依赖" -Command @(
        $python, "-m", "pip", "install", "--force-reinstall", "--no-deps",
        "xformers==0.0.35", "--index-url", "https://download.pytorch.org/whl/cu128"
    ) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 Triton Windows 加速依赖" -Command (@(
        $python, "-m", "pip", "install", "--upgrade"
    ) + $pipMirror + @("triton-windows==3.6.0.post26")) -TimeoutSeconds 0 | Out-Null
    Invoke-Native -Label "安装 LyCORIS 训练依赖" -Command (@(
        $python, "-m", "pip", "install", "--upgrade"
    ) + $pipMirror + @("lycoris-lora")) -TimeoutSeconds 0 | Out-Null

    Validate-SdScriptsWindows
    Save-BackendManifest -BackendId "sd-scripts" -RepoUrl $SdScriptsRepoUrl -Revision (Get-WindowsRevision $git $SdScriptsWindowsRepo) -RuntimeTarget "windows" -InterpreterPath $python -InstallRoot $ManagedRoot
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
    if ((Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath (Join-Path $SdScriptsWindowsRepo "train_network.py"))) {
        Write-Host "sd-scripts Windows：已安装"
        Invoke-OptionalNative -Label "读取 sd-scripts Python 版本" -Command @($python, "--version") -TimeoutSeconds 30
    }
    else {
        Write-Host "sd-scripts Windows：未安装或文件不完整"
    }
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
    $git = Ensure-WindowsGit
    $uv = Ensure-WindowsUv
    Sync-WindowsRepository -Git $git -RepoUrl $AnimaFastLoraRepoUrl -RepoPath $AnimaFastLoraRepo -Reinstall $Reinstall -UpdateOnly $UpdateOnly
    Ensure-AnimaFastWindowsUvEnvironment

    $uvArgs = @($uv, "sync", "--project", $AnimaFastLoraTrainRoot, "--python", "3.13", "--no-dev", "--no-default-groups")
    if ($MirrorProfile.Pip) {
        $env:UV_DEFAULT_INDEX = [string]$MirrorProfile.Pip
    }
    try {
        Invoke-Native -Label "安装 Anima Fast LoRA Python 3.13 依赖" -Command $uvArgs -TimeoutSeconds 0 | Out-Null
    }
    finally {
        Remove-Item Env:\UV_DEFAULT_INDEX -ErrorAction SilentlyContinue
    }

    Validate-AnimaFastLoraWindows
    $python = Join-Path $AnimaFastLoraTrainRoot ".venv\Scripts\python.exe"
    Save-BackendManifest -BackendId "anima-fast-lora" -RepoUrl $AnimaFastLoraRepoUrl -Revision (Get-WindowsRevision $git $AnimaFastLoraRepo) -RuntimeTarget "windows" -InterpreterPath $python -InstallRoot $ManagedRoot
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
    if ((Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath (Join-Path $AnimaFastLoraTrainRoot "train.py"))) {
        Write-Host "Anima Fast LoRA Windows：已安装"
        Invoke-OptionalNative -Label "读取 Anima Fast LoRA Python 版本" -Command @($python, "--version") -TimeoutSeconds 30
    }
    else {
        Write-Host "Anima Fast LoRA Windows：未安装或文件不完整"
    }
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
    if (-not (Test-Wsl2Available) -or -not (Test-UbuntuInstalled)) {
        Write-Host "wsl-sd-scripts：WSL2 / Ubuntu 不可用"
        return
    }
    $conda = Get-CondaSourceSnippet
    try {
        Invoke-WslBash -Label "读取 wsl-sd-scripts 状态" -Script "test -f $WslSdScriptsRepo/train_network.py && $conda && conda run -n $WslSdScriptsEnv python --version" -TimeoutSeconds 30 | Out-Null
        Write-Host "wsl-sd-scripts：已安装"
    }
    catch {
        Write-Host "wsl-sd-scripts：未安装或环境不完整"
    }
}

function Show-WslBaseStatus {
    Write-Host "托管目录：$ManagedRoot"
    if (-not (Test-WslExe)) {
        Write-Host "WSL 基础环境：未找到 wsl.exe"
        return
    }
    Invoke-Native -Label "WSL 发行版状态" -Command @("wsl.exe", "-l", "-v") -TimeoutSeconds 30 -NativeOutputEncoding ([Text.Encoding]::Unicode) -Optional | Out-Null
    if (Test-UbuntuInstalled) {
        Write-Host "Ubuntu 子系统：已安装"
        if (Test-UbuntuRootReady) { Write-Host "Ubuntu root shell：可用" }
        else { Write-Host "Ubuntu root shell：尚未初始化或不可用" }
    }
    else {
        Write-Host "Ubuntu 子系统：未安装"
    }
}

function Invoke-WslBaseAction {
    switch ($Action) {
        "Check" { Show-WslBaseStatus }
        "InstallWsl" { Install-WindowsWslFeature }
        "InstallUbuntu" { Install-UbuntuDistribution }
        "Install" {
            Install-WindowsWslFeature
            if (Test-WslExe) { Install-UbuntuDistribution }
        }
        "Update" { Install-WindowsWslFeature; if (Test-WslExe) { Install-UbuntuDistribution } }
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
                "Check" { Show-Status }
                "InstallWsl" { Install-WindowsWslFeature }
                "InstallUbuntu" { Install-UbuntuDistribution }
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
                "Update" { Install-SdScriptsWindows -UpdateOnly:$true }
                "Reinstall" { Install-SdScriptsWindows -Reinstall:$true }
                "Validate" { Validate-SdScriptsWindows }
                default { throw "sd-scripts Windows 不支持操作：$Action" }
            }
        }
        "anima-fast-lora" {
            switch ($Action) {
                "Check" { Show-AnimaFastLoraWindowsStatus }
                "Install" { Install-AnimaFastLoraWindows }
                "Update" { Install-AnimaFastLoraWindows -UpdateOnly:$true }
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
