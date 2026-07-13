param(
    [Parameter(Mandatory = $true)]
    [string]$DevenvPath,
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $installerDir 'CodexQuotaPanelSetup.vdproj'
$solutionPath = Join-Path $installerDir 'CodexQuotaPanelInstaller.sln'
$generatedProject = Join-Path $installerDir 'CodexQuotaPanelSetup.en-us.generated.vdproj'
$generatedSolution = Join-Path $installerDir 'CodexQuotaPanelInstaller.en-us.generated.sln'
$iconPath = Join-Path $installerDir '..\CodexQuotaPanel\Assets\CodexQuotaPanel.ico'
$postProcessor = Join-Path $installerDir 'Set-OptionalDesktopShortcut.ps1'
$baseMsi = Join-Path $installerDir "$Configuration\CodexQuotaPanel-0.1.1-x64.msi"
$englishMsi = Join-Path $installerDir "$Configuration-en-us\CodexQuotaPanel-0.1.1-en-us-x64.msi"
$transformPath = Join-Path $installerDir "$Configuration\CodexQuotaPanel-0.1.1-en-us.mst"
$validationMsi = Join-Path $installerDir '..\stability-qa\installer-language-transform-validation.msi'

function Invoke-InstallerBuild {
    param([string]$Solution)

    & $DevenvPath $Solution /Rebuild "$Configuration|Default"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Visual Studio installer build failed with exit code ${LASTEXITCODE}: $Solution"
    }
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

function Get-MsiScalar {
    param(
        [Parameter(Mandatory = $true)]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $view = Invoke-ComMethod $Database 'OpenView' @($Sql)
    $record = $null
    try
    {
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
        [void](Invoke-ComMethod $view 'Close')
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function New-EnglishProject {
    $text = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    $replacements = [ordered]@{
        '"ProjectName" = "8:CodexQuotaPanelSetup"' = '"ProjectName" = "8:CodexQuotaPanelSetupEnUs"'
        '"LanguageId" = "3:2052"' = '"LanguageId" = "3:1033"'
        '"UILanguageId" = "3:2052"' = '"UILanguageId" = "3:1033"'
        'Debug\\CodexQuotaPanel-0.1.1-x64.msi' = 'Debug-en-us\\CodexQuotaPanel-0.1.1-en-us-x64.msi'
        'Release\\CodexQuotaPanel-0.1.1-x64.msi' = 'Release-en-us\\CodexQuotaPanel-0.1.1-en-us-x64.msi'
        '"Description" = "8:Codex 额度悬浮球与托盘面板"' = '"Description" = "8:Codex quota orb and tray panel"'
        '"LangId" = "3:2052"' = '"LangId" = "3:1033"'
        '"Title" = "8:Codex 额度面板安装程序"' = '"Title" = "8:CodexQuotaPanel Installer"'
        '"Subject" = "8:本地 Codex 额度面板"' = '"Subject" = "8:Local Codex quota panel"'
        '"Keywords" = "8:Codex 额度面板"' = '"Keywords" = "8:Codex quota panel"'
        '"ARPCOMMENTS" = "8:适用于 Windows 10 和 Windows 11 的本地额度悬浮球"' = '"ARPCOMMENTS" = "8:Local-only quota orb for Windows 10 and Windows 11"'
        '"DisplayName" = "8:欢迎"' = '"DisplayName" = "8:Welcome"'
        '"DisplayName" = "8:确认安装"' = '"DisplayName" = "8:Confirm Installation"'
        '"DisplayName" = "8:安装选项"' = '"DisplayName" = "8:Installation Options"'
        '"Value" = "8:安装选项"' = '"Value" = "8:Installation options"'
        '"Value" = "8:请选择安装程序要创建的快捷方式。"' = '"Value" = "8:Choose the shortcuts you want the installer to create."'
        '"Value" = "8:创建桌面快捷方式"' = '"Value" = "8:Create a desktop shortcut"'
        '"DisplayName" = "8:安装文件夹"' = '"DisplayName" = "8:Installation Folder"'
        '"DisplayName" = "8:安装进度"' = '"DisplayName" = "8:Progress"'
        '"DisplayName" = "8:安装完成"' = '"DisplayName" = "8:Finished"'
    }

    foreach ($entry in $replacements.GetEnumerator())
    {
        if (-not $text.Contains($entry.Key))
        {
            throw "Localized installer source token was not found: $($entry.Key)"
        }
        $text = $text.Replace($entry.Key, $entry.Value)
    }

    [IO.File]::WriteAllText($generatedProject, $text, [Text.UTF8Encoding]::new($true))

    $solution = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    $solution = $solution.Replace(
        '"CodexQuotaPanelSetup", "CodexQuotaPanelSetup.vdproj", "{956DA40F-62E0-496D-8861-2AF9DBD1C2EC}"',
        '"CodexQuotaPanelSetupEnUs", "CodexQuotaPanelSetup.en-us.generated.vdproj", "{4DAF16CF-505F-4FEC-9F28-7FD152FB0732}"')
    $solution = $solution.Replace(
        '{956DA40F-62E0-496D-8861-2AF9DBD1C2EC}',
        '{4DAF16CF-505F-4FEC-9F28-7FD152FB0732}')
    [IO.File]::WriteAllText($generatedSolution, $solution, [Text.UTF8Encoding]::new($true))
}

function New-EnglishTransform {
    if (Test-Path -LiteralPath $transformPath) { Remove-Item -LiteralPath $transformPath -Force }

    $installer = $null
    $englishDb = $null
    $baseDb = $null
    try
    {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $englishDb = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $englishMsi).Path, 0)
        $baseDb = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $baseMsi).Path, 0)
        $generated = $englishDb.GenerateTransform($baseDb, $transformPath)
        if (-not $generated -or -not (Test-Path -LiteralPath $transformPath))
        {
            throw 'Windows Installer did not generate the English language transform.'
        }
        [void]$englishDb.CreateTransformSummaryInfo($baseDb, $transformPath, 0, 0)
    }
    finally
    {
        if ($null -ne $baseDb) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($baseDb) }
        if ($null -ne $englishDb) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($englishDb) }
        if ($null -ne $installer) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }
}

function Test-EnglishTransform {
    $validationDir = Split-Path -Parent $validationMsi
    New-Item -ItemType Directory -Path $validationDir -Force | Out-Null
    Copy-Item -LiteralPath $baseMsi -Destination $validationMsi -Force

    $installer = $null
    $database = $null
    try
    {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $validationMsi).Path, 1)
        [void]$database.ApplyTransform((Resolve-Path -LiteralPath $transformPath).Path, 0)
        [void](Invoke-ComMethod $database 'Commit')

        $language = Get-MsiScalar $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductLanguage'"
        $options = Get-MsiScalar $database "SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='CustomCheckA' AND ``Control``='BannerText'"
        $confirmation = Get-MsiScalar $database "SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='ConfirmInstallForm' AND ``Control``='BannerText'"
        if ($language -ne '1033' -or $options -notlike '*Installation options*' -or
            $confirmation -notlike '*Confirm Installation*')
        {
            throw "English transform validation failed: language=$language options=$options confirmation=$confirmation"
        }
    }
    finally
    {
        if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
        if ($null -ne $installer) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }
}

$resolvedDevenv = (Resolve-Path -LiteralPath $DevenvPath).Path
New-EnglishProject
Invoke-InstallerBuild $solutionPath
& $postProcessor -MsiPath $baseMsi -IconPath $iconPath
if ($LASTEXITCODE -ne 0) { throw "Chinese MSI post-processing failed with exit code $LASTEXITCODE" }

Invoke-InstallerBuild $generatedSolution
& $postProcessor -MsiPath $englishMsi -IconPath $iconPath
if ($LASTEXITCODE -ne 0) { throw "English MSI post-processing failed with exit code $LASTEXITCODE" }

New-EnglishTransform
Test-EnglishTransform
Write-Output "PASS localized installer | zh-CN base + en-US transform | $transformPath"
