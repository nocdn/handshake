[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $Version = "",
    [string] $VersionInfoVersion = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $repoRoot "src\HsWin.App\HsWin.App.csproj"
$cliProjectPath = Join-Path $repoRoot "src\HsWin.Cli\HsWin.Cli.csproj"
$installerScriptPath = Join-Path $repoRoot "installer\HsWin.iss"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$appPublishDir = Join-Path $artifactsRoot "publish\HsWin.App\$Configuration\$RuntimeIdentifier"
$cliPublishDir = Join-Path $artifactsRoot "publish\HsWin.Cli\$Configuration\$RuntimeIdentifier"
$stagingDir = Join-Path $artifactsRoot "publish\installer\$Configuration\$RuntimeIdentifier"
$installerOutputDir = Join-Path $artifactsRoot "installer"

# Releases are numbered by the CI run (Version "31" -> file version 0.0.0.31). Local builds are
# development builds: file version 0.0.0.0, so they always compare older than any release and
# installing one over the other is a predictable downgrade or upgrade, never a coin flip.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.0-dev.{0:yyyyMMddHHmmss}" -f [System.DateTimeOffset]::Now
    if ([string]::IsNullOrWhiteSpace($VersionInfoVersion)) {
        $VersionInfoVersion = "0.0.0.0"
    }
}

if ([string]::IsNullOrWhiteSpace($VersionInfoVersion)) {
    if ($Version -match '^\d+$') {
        $VersionInfoVersion = "0.0.0.$Version"
    }
    elseif ($Version -match '^\d+\.\d+\.\d+$') {
        $VersionInfoVersion = "$Version.0"
    }
    elseif ($Version -match '^\d+\.\d+\.\d+\.\d+$') {
        $VersionInfoVersion = $Version
    }
    else {
        throw "Version '$Version' is not a release number; pass -VersionInfoVersion explicitly."
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Parent,

        [Parameter(Mandatory = $true)]
        [string] $Child
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)

    if (-not $resolvedChild.StartsWith($resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate on '$resolvedChild' because it is outside '$resolvedParent'."
    }
}

function Get-InnoSetupCompiler {
    $candidates = @()

    if ($env:INNO_SETUP_ISCC) {
        $candidates += $env:INNO_SETUP_ISCC
    }

    $pathCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($pathCommand) {
        $candidates += $pathCommand.Source
    }

    $candidates += @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files\Inno Setup 7\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Inno Setup Compiler (ISCC.exe) was not found. Install Inno Setup or set INNO_SETUP_ISCC to the full path of ISCC.exe."
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Project,

        [Parameter(Mandatory = $true)]
        [string] $Output
    )

    dotnet publish $Project `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $Output `
        -p:PublishSingleFile=false `
        -p:Version=$Version `
        -p:AssemblyVersion=$VersionInfoVersion `
        -p:FileVersion=$VersionInfoVersion `
        -p:InformationalVersion=$Version

    # $ErrorActionPreference does not apply to native commands.
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for '$Project' with exit code $LASTEXITCODE."
    }
}

# The WPF app ships the Windows Desktop runtime, whose copies of these files are complete; the
# console CLI ships thin facades of the same names. Where they differ, the app's copy must win.
$desktopRuntimeFiles = @('Microsoft.VisualBasic.dll', 'System.Drawing.dll', 'WindowsBase.dll')

# Both apps are self-contained and ship overlapping runtime files. Merge one publish into the
# staging folder, failing on any conflicting file instead of letting one publish silently
# overwrite another. Files listed in -KeepExisting keep the copy that is already staged.
function Merge-PublishOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination,

        [string[]] $KeepExisting = @()
    )

    $sourceRoot = [System.IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse) {
        $relativePath = $file.FullName.Substring($sourceRoot.Length)
        $target = Join-Path $Destination $relativePath
        if (Test-Path -LiteralPath $target) {
            $sourceHash = (Get-FileHash -LiteralPath $file.FullName).Hash
            $targetHash = (Get-FileHash -LiteralPath $target).Hash
            if ($sourceHash -ne $targetHash -and $KeepExisting -notcontains $relativePath) {
                throw "The app and CLI publish different versions of '$relativePath'; align their package versions."
            }

            continue
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

foreach ($directory in @($appPublishDir, $cliPublishDir, $stagingDir)) {
    Assert-ChildPath -Parent $repoRoot -Child $directory
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

Assert-ChildPath -Parent $repoRoot -Child $installerOutputDir
New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null

Invoke-Publish -Project $projectPath -Output $appPublishDir
Invoke-Publish -Project $cliProjectPath -Output $cliPublishDir
Merge-PublishOutput -Source $appPublishDir -Destination $stagingDir
Merge-PublishOutput -Source $cliPublishDir -Destination $stagingDir -KeepExisting $desktopRuntimeFiles

$iscc = Get-InnoSetupCompiler

$previousVersion = $env:HSWIN_VERSION
$previousVersionInfoVersion = $env:HSWIN_VERSION_INFO_VERSION
$previousPublishDir = $env:HSWIN_PUBLISH_DIR
$previousOutputDir = $env:HSWIN_OUTPUT_DIR

try {
    $env:HSWIN_VERSION = $Version
    $env:HSWIN_VERSION_INFO_VERSION = $VersionInfoVersion
    $env:HSWIN_PUBLISH_DIR = $stagingDir
    $env:HSWIN_OUTPUT_DIR = $installerOutputDir

    & $iscc $installerScriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup Compiler failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:HSWIN_VERSION = $previousVersion
    $env:HSWIN_VERSION_INFO_VERSION = $previousVersionInfoVersion
    $env:HSWIN_PUBLISH_DIR = $previousPublishDir
    $env:HSWIN_OUTPUT_DIR = $previousOutputDir
}

$installerPath = Join-Path $installerOutputDir "hswin-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Expected installer was not created: $installerPath"
}

Write-Output $installerPath
