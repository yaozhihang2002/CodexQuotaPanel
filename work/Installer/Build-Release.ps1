[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.4.1',
    [string]$DevenvPath,
    [string]$OutputDirectory,
    [switch]$PublishToGitHub,
    [string]$PublishConfirmation = 'NO',
    [string]$GitHubRepository = 'yaozhihang2002/CodexQuotaPanel',
    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $installerDirectory '..\..'))
$applicationProject = Join-Path $repositoryRoot 'work\CodexQuotaPanel\CodexQuotaPanel.csproj'
$testProject = Join-Path $repositoryRoot 'work\CodexQuotaPanel.Tests\CodexQuotaPanel.Tests.csproj'
$testExecutable = Join-Path $repositoryRoot 'work\CodexQuotaPanel.Tests\bin\Release\net9.0-windows\CodexQuotaPanel.Tests.exe'
$stageDirectory = Join-Path $repositoryRoot 'work\installer-stage\win-x64'
$candidateDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    Join-Path $repositoryRoot "outputs\release-v$Version"
}
else
{
    [IO.Path]::GetFullPath($OutputDirectory)
}
$localizedInstallerScript = Join-Path $installerDirectory 'Build-LocalizedInstaller.ps1'
$launcherScript = Join-Path $installerDirectory 'Build-LanguageSetupLauncher.ps1'
$installerProject = Join-Path $installerDirectory 'CodexQuotaPanelSetup.vdproj'
$timings = [ordered]@{}
$totalStopwatch = [Diagnostics.Stopwatch]::StartNew()

if ($PublishToGitHub -and $PublishConfirmation -cne "PUBLISH v$Version")
{
    throw "GitHub publishing requires -PublishConfirmation 'PUBLISH v$Version'."
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "==> $Name"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    & $Action
    if ($LASTEXITCODE -ne 0)
    {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
    $stopwatch.Stop()
    $timings[$Name] = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
}

function Reset-RepositoryDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootPrefix = $repositoryRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to reset a directory outside the repository: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath)
    {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Resolve-Devenv {
    if (-not [string]::IsNullOrWhiteSpace($DevenvPath))
    {
        return (Resolve-Path -LiteralPath $DevenvPath).Path
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere))
    {
        throw 'Visual Studio locator was not found. Install Visual Studio 2022 and Installer Projects, or pass -DevenvPath.'
    }
    $installationPath = & $vswhere -latest -products * -version '[17.0,18.0)' -property installationPath
    if ([string]::IsNullOrWhiteSpace($installationPath))
    {
        throw 'Visual Studio 2022 was not found. Pass -DevenvPath if it is installed in a custom location.'
    }
    $candidate = Join-Path $installationPath 'Common7\IDE\devenv.com'
    if (-not (Test-Path -LiteralPath $candidate))
    {
        throw "devenv.com was not found under $installationPath."
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Assert-VersionSynchronization {
    [xml]$project = Get-Content -LiteralPath $applicationProject -Raw -Encoding UTF8
    $projectVersion = [string]$project.Project.PropertyGroup.Version
    $assemblyVersion = [string]$project.Project.PropertyGroup.AssemblyVersion
    $fileVersion = [string]$project.Project.PropertyGroup.FileVersion
    $informationalVersion = [string]$project.Project.PropertyGroup.InformationalVersion
    if ($projectVersion -cne $Version -or
        $assemblyVersion -cne "$Version.0" -or
        $fileVersion -cne "$Version.0" -or
        $informationalVersion -cne $Version)
    {
        throw "Application version metadata is not synchronized with $Version."
    }

    $installerText = Get-Content -LiteralPath $installerProject -Raw -Encoding UTF8
    if (-not $installerText.Contains('"ProductVersion" = "8:' + $Version + '"') -or
        -not $installerText.Contains("CodexQuotaPanel-$Version-x64.msi"))
    {
        throw "Installer project version does not match $Version."
    }

    $status = & git -C $repositoryRoot status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Git working tree.' }
    if ($status)
    {
        throw 'Release packaging requires a clean Git working tree so the source archive matches the binaries.'
    }
}

Push-Location $repositoryRoot
try
{
    Assert-VersionSynchronization
    $resolvedDevenv = Resolve-Devenv
    Reset-RepositoryDirectory $stageDirectory
    Reset-RepositoryDirectory $candidateDirectory

    Invoke-CheckedCommand 'Restore once' {
        & dotnet restore $testProject -r win-x64 -p:SelfContained=true
    }
    Invoke-CheckedCommand 'Build application and tests once' {
        & dotnet build $testProject -c $Configuration -r win-x64 --self-contained true --no-restore
    }
    Invoke-CheckedCommand 'Run deterministic regression checks' {
        & $testExecutable
    }
    Invoke-CheckedCommand 'Run targeted release checks' {
        & $testExecutable --targeted-check
    }
    Invoke-CheckedCommand 'Publish from the existing build' {
        & dotnet publish $applicationProject `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            --no-build `
            --no-restore `
            -o $stageDirectory
    }

    $applicationBinary = Join-Path $stageDirectory 'CodexQuotaPanel.exe'
    if (-not (Test-Path -LiteralPath $applicationBinary))
    {
        throw "Published application was not found: $applicationBinary"
    }
    $publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($applicationBinary).FileVersion
    if ($publishedVersion -cne "$Version.0")
    {
        throw "Published application version is $publishedVersion instead of $Version.0."
    }
    $payloadHashBeforePackaging = (Get-FileHash -LiteralPath $applicationBinary -Algorithm SHA256).Hash

    Invoke-CheckedCommand 'Build bilingual installer from staged payload' {
        & $localizedInstallerScript `
            -DevenvPath $resolvedDevenv `
            -Configuration $Configuration `
            -Version $Version
    }

    $portable = Join-Path $candidateDirectory "CodexQuotaPanel-$Version-portable-x64.zip"
    $setup = Join-Path $candidateDirectory "CodexQuotaPanel-$Version-Setup.exe"
    $source = Join-Path $candidateDirectory "CodexQuotaPanel-$Version-source.zip"
    $msi = Join-Path $candidateDirectory "CodexQuotaPanel-$Version-x64.msi"
    $builtMsi = Join-Path $installerDirectory "$Configuration\CodexQuotaPanel-$Version-x64.msi"
    $transform = Join-Path $installerDirectory "$Configuration\CodexQuotaPanel-$Version-en-us.mst"

    Invoke-CheckedCommand 'Assemble attachments from reused payload' {
        Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $portable -CompressionLevel Optimal
        Copy-Item -LiteralPath $builtMsi -Destination $msi
        & $launcherScript `
            -MsiPath $builtMsi `
            -EnglishTransformPath $transform `
            -OutputPath $setup `
            -Version $Version
        if ($LASTEXITCODE -ne 0) { throw "Setup launcher failed with exit code $LASTEXITCODE." }
        & git archive --format=zip --output=$source HEAD
        if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE." }
    }

    $attachments = @($setup, $portable, $source, $msi)
    foreach ($attachment in $attachments)
    {
        if (-not (Test-Path -LiteralPath $attachment))
        {
            throw "Expected release attachment was not created: $attachment"
        }
    }
    if ((Get-FileHash -LiteralPath $applicationBinary -Algorithm SHA256).Hash -cne $payloadHashBeforePackaging)
    {
        throw 'The staged application payload changed while installer and portable attachments were assembled.'
    }

    $checksumPath = Join-Path $candidateDirectory "SHA256SUMS-v$Version.txt"
    $hashLines = $attachments |
        ForEach-Object { Get-Item -LiteralPath $_ } |
        Sort-Object Name |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.Name)"
        }
    [IO.File]::WriteAllLines($checksumPath, $hashLines, [Text.UTF8Encoding]::new($false))

    $actualNames = Get-ChildItem -LiteralPath $candidateDirectory -File |
        Sort-Object Name |
        Select-Object -ExpandProperty Name
    $expectedNames = @(
        "CodexQuotaPanel-$Version-Setup.exe",
        "CodexQuotaPanel-$Version-portable-x64.zip",
        "CodexQuotaPanel-$Version-source.zip",
        "CodexQuotaPanel-$Version-x64.msi",
        "SHA256SUMS-v$Version.txt"
    ) | Sort-Object
    if (Compare-Object $expectedNames $actualNames)
    {
        throw "Release attachment set is not exact: $($actualNames -join ', ')"
    }

    if ($PublishToGitHub)
    {
        $tag = "v$Version"
        $tagCommit = (& git rev-list -n 1 $tag).Trim()
        $headCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit) -or $tagCommit -cne $headCommit)
        {
            throw "Publishing requires tag $tag to exist at the current HEAD."
        }
        $releaseNotes = Join-Path $repositoryRoot "docs\releases\$tag.md"
        if (-not (Test-Path -LiteralPath $releaseNotes))
        {
            throw "Release notes were not found: $releaseNotes"
        }
        & gh auth status --hostname github.com
        if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated.' }
        & gh release view $tag --repo $GitHubRepository *> $null
        if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists; refusing to overwrite it." }
        $releaseAssets = @($attachments) + $checksumPath
        & gh release create $tag $releaseAssets `
            --repo $GitHubRepository `
            --verify-tag `
            --prerelease `
            --title "$tag Pre-release" `
            --notes-file $releaseNotes
        if ($LASTEXITCODE -ne 0) { throw "GitHub release creation failed with exit code $LASTEXITCODE." }
        Write-Output "PUBLISHED https://github.com/$GitHubRepository/releases/tag/$tag"
    }

    $totalStopwatch.Stop()
    Write-Output "PASS local release v$Version | application-builds=1 | reused-payload-sha256=$($payloadHashBeforePackaging.ToLowerInvariant())"
    Write-Output "OUTPUT $candidateDirectory"
    Write-Output "TIMINGS $([System.Text.Json.JsonSerializer]::Serialize($timings)) total=$([math]::Round($totalStopwatch.Elapsed.TotalSeconds, 2))s"
}
finally
{
    Pop-Location
}
