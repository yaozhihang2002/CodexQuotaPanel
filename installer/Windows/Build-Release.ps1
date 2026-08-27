[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.6.2',
    [string]$DotNetPath,
    [string]$DevenvPath,
    [string]$OutputDirectory,
    [switch]$SkipChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installerDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $installerDirectory '..\..'))
$solution = Join-Path $repositoryRoot 'CodexQuotaPanel.VNext.slnx'
$aggregate = Join-Path $repositoryRoot 'build\CodexQuota.ReleaseAggregate.csproj'
$appProject = Join-Path $repositoryRoot 'src\CodexQuota.App\CodexQuota.App.csproj'
$stage = Join-Path $repositoryRoot 'artifacts\release-stage\win-x64'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {
    Join-Path $repositoryRoot "artifacts\release-v$Version"
}
$installerProject = Join-Path $installerDirectory 'CodexQuotaPanelSetup.vdproj'
$localizedBuilder = Join-Path $installerDirectory 'Build-LocalizedInstaller.ps1'
$launcherBuilder = Join-Path $installerDirectory 'Build-LanguageSetupLauncher.ps1'
$timer = [Diagnostics.Stopwatch]::StartNew()

function Reset-LocalDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $repositoryRoot.TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the repository: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    New-Item -ItemType Directory -Path $full -Force | Out-Null
}

function Resolve-Devenv {
    if ($DevenvPath) { return (Resolve-Path -LiteralPath $DevenvPath).Path }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio locator was not found.' }
    $installation = & $vswhere -latest -products * -version '[17.0,18.0)' -property installationPath
    $candidate = Join-Path $installation 'Common7\IDE\devenv.com'
    if (-not (Test-Path -LiteralPath $candidate)) { throw 'Visual Studio 2022 devenv.com was not found.' }
    return $candidate
}

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE" }
}

[xml]$project = Get-Content -LiteralPath $appProject -Raw -Encoding UTF8
if ([string]$project.Project.PropertyGroup.Version -cne $Version) { throw 'Application version is not synchronized.' }
$installerText = Get-Content -LiteralPath $installerProject -Raw -Encoding UTF8
if (-not $installerText.Contains('"ProductVersion" = "8:' + $Version + '"')) { throw 'Installer version is not synchronized.' }

Reset-LocalDirectory $stage
Reset-LocalDirectory $output
$dotnet = if ($DotNetPath) { (Resolve-Path -LiteralPath $DotNetPath).Path } else {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw 'dotnet SDK was not found. Pass -DotNetPath or add dotnet to PATH.' }
    $command.Source
}

if (-not $SkipChecks) {
    Invoke-Step 'Restore once' { & $dotnet restore $aggregate -r win-x64 }
    Invoke-Step 'Build once' {
        & $dotnet build $aggregate -c Release -r win-x64 --no-restore -p:PublishSingleFile=true
    }
    foreach ($test in @('Domain','Application','Infrastructure','Platform','UI')) {
        $projectPath = Join-Path $repositoryRoot "tests\CodexQuota.$test.Tests\CodexQuota.$test.Tests.csproj"
        Invoke-Step "$test checks" { & $dotnet run --project $projectPath -c Release -r win-x64 --no-build --no-restore }
    }
}

Invoke-Step 'Publish one framework-dependent application payload' {
    & $dotnet publish $appProject -c Release -r win-x64 --self-contained false `
        --no-build --no-restore `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $stage
}
$exe = Join-Path $stage 'CodexQuotaPanel.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published executable is missing.' }
# --no-build can retain PDB files from a prior build in the publish output.
# They are not required by end users and may contain local build-path metadata.
Get-ChildItem -LiteralPath $stage -Recurse -File -Filter '*.pdb' | Remove-Item -Force
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if ($fileVersion -cne "$Version.0") { throw "Published version mismatch: $fileVersion" }
$payloadHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash

$resolvedDevenv = Resolve-Devenv
Invoke-Step 'Build bilingual MSI from the same payload' {
    & $localizedBuilder -DevenvPath $resolvedDevenv -Configuration Release -Version $Version
}
$builtMsi = Join-Path $installerDirectory "Release\CodexQuotaPanel-$Version-x64.msi"
$transform = Join-Path $installerDirectory "Release\CodexQuotaPanel-$Version-en-us.mst"
$setup = Join-Path $output "CodexQuotaPanel-$Version-Windows-Setup.exe"
$runtimeUrl = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.11/dotnet-runtime-10.0.11-win-x64.exe'
$runtimeSha512 = '694e0e0af26b2b8949b8eda8a3831ab31aeac79797d43d6ff8c8798eae642c0904852e641c47329d7d893408f25feab1530ca2b7a0c6ed0d991e0113466a4bf9'
Invoke-Step 'Build Chinese-default web setup launcher' {
    & $launcherBuilder -MsiPath $builtMsi -EnglishTransformPath $transform -OutputPath $setup -Version $Version `
        -RequiresDotNetRuntime -RuntimeDownloadUrl $runtimeUrl -RuntimeSha512 $runtimeSha512
}
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -cne $payloadHash) {
    throw 'The application payload changed while packaging.'
}
$files = Get-ChildItem -LiteralPath $output -File | Sort-Object Name
$lines = foreach ($file in $files) {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
}
[IO.File]::WriteAllLines((Join-Path $output "SHA256SUMS-v$Version.txt"), $lines, [Text.UTF8Encoding]::new($false))
$timer.Stop()
Write-Output "PASS v$Version one-payload release candidate"
Write-Output "OUTPUT $output"
Write-Output "PAYLOAD_SHA256 $($payloadHash.ToLowerInvariant())"
Write-Output "ELAPSED_SECONDS $([math]::Round($timer.Elapsed.TotalSeconds,2))"
