param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,
    [Parameter(Mandatory = $true)]
    [string]$EnglishTransformPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.3.2'
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
$sourceText = Get-Content -LiteralPath $resolvedSource -Raw -Encoding UTF8
$expectedAssemblyVersion = "$Version.0"
if (-not $sourceText.Contains("[assembly: AssemblyVersion(`"$expectedAssemblyVersion`")]"))
{
    throw "Setup launcher source version does not match requested version $Version."
}

function Invoke-ComMethod {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [object[]]$Arguments = @()
    )

    $Object.GetType().InvokeMember(
        $Name,
        [System.Reflection.BindingFlags]::InvokeMethod,
        $null,
        $Object,
        $Arguments)
}

function Get-MsiProperty {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $installer = $null
    $database = $null
    $view = $null
    $record = $null
    try
    {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = Invoke-ComMethod $installer 'OpenDatabase' @($Path, 0)
        $view = Invoke-ComMethod $database 'OpenView' @(
            "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
        [void](Invoke-ComMethod $view 'Execute')
        $record = Invoke-ComMethod $view 'Fetch'
        if ($null -eq $record) { return $null }
        return $record.GetType().InvokeMember(
            'StringData',
            [System.Reflection.BindingFlags]::GetProperty,
            $null,
            $record,
            @(1))
    }
    finally
    {
        if ($null -ne $record)
        {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        if ($null -ne $view)
        {
            [void](Invoke-ComMethod $view 'Close')
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
        if ($null -ne $database)
        {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        }
        if ($null -ne $installer)
        {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
        }
    }
}

$productCode = Get-MsiProperty -Path $resolvedMsi -Name 'ProductCode'
if ($productCode -notmatch '^\{[0-9A-Fa-f-]{36}\}$')
{
    throw "Embedded MSI contains an invalid ProductCode: $productCode"
}
if (-not $sourceText.Contains('__MSI_PRODUCT_CODE__'))
{
    throw 'Setup launcher source is missing the generated ProductCode placeholder.'
}
$generatedSource = Join-Path ([IO.Path]::GetTempPath()) (
    'CodexQuotaPanelSetupLauncher-' + [Guid]::NewGuid().ToString('N') + '.cs')
$sourceText.Replace('__MSI_PRODUCT_CODE__', $productCode) |
    Set-Content -LiteralPath $generatedSource -Encoding UTF8

$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

try
{
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
        $generatedSource

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fullOutput))
    {
        throw "Language setup launcher compilation failed with exit code $LASTEXITCODE"
    }
}
finally
{
    Remove-Item -LiteralPath $generatedSource -Force -ErrorAction SilentlyContinue
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName($fullOutput)
if ($assembly.Version.ToString() -ne $expectedAssemblyVersion)
{
    throw "Unexpected setup launcher version: $($assembly.Version)"
}

$fileSize = (Get-Item -LiteralPath $fullOutput).Length
Write-Output "PASS setup launcher v$Version | ProductCode=$productCode | default=zh-CN + en-US option + embedded MSI/MST | bytes=$fileSize | $fullOutput"
