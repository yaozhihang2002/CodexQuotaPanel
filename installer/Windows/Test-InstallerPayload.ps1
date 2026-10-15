[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MsiPath,
    [Parameter(Mandatory=$true)][string]$EnglishTransformPath,
    [Parameter(Mandatory=$true)][string]$ExpectedExecutable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$msi=(Resolve-Path -LiteralPath $MsiPath).Path
$mst=(Resolve-Path -LiteralPath $EnglishTransformPath).Path
$expected=(Get-FileHash -LiteralPath $ExpectedExecutable -Algorithm SHA256).Hash
$root=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $root){throw 'Use a new output directory for installer extraction checks.'}
New-Item -ItemType Directory -Path $root | Out-Null
foreach($language in @('zh-CN','en-US')) {
    $destination=Join-Path $root $language
    New-Item -ItemType Directory -Path $destination | Out-Null
    $log=Join-Path $root "$language.log"
    # Administrative extraction: no product registration, no uninstall/upgrade,
    # no runtime launch. Exercise MSI CAB lookup through the real installer.
    $arguments="/a `"$msi`" /qn TARGETDIR=`"$destination`" /L*v `"$log`""
    if($language -eq 'en-US'){ $arguments += " TRANSFORMS=`"$mst`"" }
    $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
        -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if(-not $process.WaitForExit(60000)){ throw "Administrative extraction timed out; see $log" }
    if($process.ExitCode -ne 0){ throw "Administrative extraction failed ($language): $($process.ExitCode); see $log" }
    $files=@(Get-ChildItem -LiteralPath $destination -Filter CodexQuotaPanel.exe -Recurse -File)
    if($files.Count -ne 1){throw "Expected exactly one application payload for $language"}
    if((Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash -cne $expected){
        throw "Extracted application differs from staged payload: $language"
    }
    Write-Output "PASS $language Windows Installer administrative extraction and payload match"
}
