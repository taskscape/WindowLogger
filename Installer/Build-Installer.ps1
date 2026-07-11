param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use numeric major.minor.patch format (for example, 1.2.3)."
}

$installerDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Split-Path -Parent $installerDirectory
$stagingDirectory = Join-Path $installerDirectory "Staging"
$publishRoot = Join-Path $installerDirectory "Publish"
$outputDirectory = Join-Path $installerDirectory "Output"
$installerScript = Join-Path $installerDirectory "Installer.iss"

function Reset-BuildDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $allowedRoot = [IO.Path]::GetFullPath($installerDirectory) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the installer workspace: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -Path $fullPath -ItemType Directory -Force | Out-Null
}

function Resolve-InnoSetupCompiler([string]$RequestedPath) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Inno Setup 6 compiler was not found. Install Inno Setup or pass -InnoSetupCompiler <path-to-ISCC.exe>."
}

Write-Host "Building WindowLogger $Version ($Configuration, $Runtime)" -ForegroundColor Cyan
Reset-BuildDirectory $stagingDirectory
Reset-BuildDirectory $publishRoot
New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null

$projects = @(
    @{ Name = "WindowLogger"; Path = "WindowLogger\WindowLogger.csproj" },
    @{ Name = "WindowAnalyser"; Path = "WindowAnalyser\WindowAnalyser.csproj" },
    @{ Name = "WindowLoggerConfigGui"; Path = "WindowLoggerConfigGui\WindowLoggerConfigGui.csproj" },
    @{ Name = "WindowLoggerTray"; Path = "WindowLoggerTray\WindowLoggerTray.csproj" }
)

foreach ($project in $projects) {
    $projectPath = Join-Path $solutionDirectory $project.Path
    $componentOutput = Join-Path $publishRoot $project.Name
    Write-Host "Publishing $($project.Name)..." -ForegroundColor Yellow

    dotnet publish $projectPath `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $componentOutput `
        -p:Version=$Version `
        -p:AssemblyVersion="$Version.0" `
        -p:FileVersion="$Version.0" `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:SatelliteResourceLanguages=pl `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $($project.Name) failed with exit code $LASTEXITCODE."
    }

    Copy-Item -Path (Join-Path $componentOutput "*") -Destination $stagingDirectory -Recurse -Force
}

$requiredFiles = @(
    "WindowLogger.exe",
    "WindowAnalyser.exe",
    "WindowLoggerConfigGui.exe",
    "WindowLoggerTray.exe",
    "WindowLogger.Shared.dll",
    "appsettings.json"
)
foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $stagingDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required installer input was not produced: $requiredPath"
    }
}

$compiler = Resolve-InnoSetupCompiler $InnoSetupCompiler
Write-Host "Compiling installer with $compiler" -ForegroundColor Yellow
& $compiler "/DMyAppVersion=$Version" "/DMyAppPath=$stagingDirectory" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputDirectory "WindowLogger-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The installer was not created at the expected path: $installerPath"
}

$installer = Get-Item -LiteralPath $installerPath
Write-Host "Installer created: $($installer.FullName) ($([Math]::Round($installer.Length / 1MB, 2)) MB)" -ForegroundColor Green
