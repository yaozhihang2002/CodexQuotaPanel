param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,
    [Parameter(Mandatory = $true)]
    [string]$EnglishTransformPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $installerDir 'LanguageSetupLauncher.cs'
$iconPath = Join-Path $installerDir '..\CodexQuotaPanel\Assets\CodexQuotaPanel.ico'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler))
{
    $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

$resolvedCompiler = (Resolve-Path -LiteralPath $compiler).Path
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$resolvedTransform = (Resolve-Path -LiteralPath $EnglishTransformPath).Path
$resolvedSource = (Resolve-Path -LiteralPath $sourcePath).Path
$resolvedIcon = (Resolve-Path -LiteralPath $iconPath).Path
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $resolvedCompiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    "/win32icon:$resolvedIcon" `
    "/out:$fullOutput" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/resource:$resolvedMsi,CodexQuotaPanel.Installer.zh-cn.msi" `
    "/resource:$resolvedTransform,CodexQuotaPanel.Installer.en-us.mst" `
    $resolvedSource

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fullOutput))
{
    throw "Language setup launcher compilation failed with exit code $LASTEXITCODE"
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName($fullOutput)
if ($assembly.Version.ToString() -ne '1.8.7.0')
{
    throw "Unexpected setup launcher version: $($assembly.Version)"
}

$fileSize = (Get-Item -LiteralPath $fullOutput).Length
Write-Output "PASS setup launcher | default=zh-CN + en-US option + embedded MSI/MST | bytes=$fileSize | $fullOutput"
