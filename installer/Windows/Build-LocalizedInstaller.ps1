param(
    [Parameter(Mandatory = $true)]
    [string]$DevenvPath,
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.8.4',
    [switch]$ReuseBuiltMsi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $installerDir 'CodexQuotaPanelSetup.vdproj'
$solutionPath = Join-Path $installerDir 'CodexQuotaPanelInstaller.sln'
$generatedProject = Join-Path $installerDir "CodexQuotaPanelSetup.en-us.v$Version.generated.vdproj"
$generatedSolution = Join-Path $installerDir "CodexQuotaPanelInstaller.en-us.v$Version.generated.sln"
$iconPath = Join-Path $installerDir '..\..\src\CodexQuota.App\Assets\CodexQuotaPanel.ico'
$postProcessor = Join-Path $installerDir 'Set-OptionalDesktopShortcut.ps1'
$upgradeCoordinatorSource = Join-Path $installerDir 'UpgradeCoordinator.cs'
$upgradeCoordinatorBinary = Join-Path $installerDir "..\..\artifacts\release-stage-v$Version\UpgradeCoordinator.exe"
$applicationBinary = Join-Path $installerDir "..\..\artifacts\release-stage-v$Version\win-x64\CodexQuotaPanel.exe"
$baseMsi = Join-Path $installerDir "$Configuration\CodexQuotaPanel-$Version-x64.msi"
$englishMsi = Join-Path $installerDir "$Configuration-en-us\CodexQuotaPanel-$Version-en-us-x64.msi"
$transformPath = Join-Path $installerDir "$Configuration\CodexQuotaPanel-$Version-en-us.mst"
$validationMsi = Join-Path $installerDir '..\..\artifacts\stability-qa\installer-language-transform-validation.msi'
$localizedMsi = Join-Path $installerDir "$Configuration\CodexQuotaPanel-$Version-localized.msi"

function Invoke-InstallerBuild {
    param([string]$Solution)

    $arguments = @(
        "`"$Solution`"",
        '/Rebuild',
        "`"$Configuration|Default`""
    )
    $process = Start-Process `
        -FilePath $DevenvPath `
        -ArgumentList $arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0)
    {
        throw "Visual Studio installer build failed with exit code $($process.ExitCode): $Solution"
    }
}

function Build-UpgradeCoordinator {
    $compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler))
    {
        $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    $outputDirectory = Split-Path -Parent $upgradeCoordinatorBinary
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    & $compiler `
        /nologo `
        /target:winexe `
        /optimize+ `
        /platform:anycpu `
        "/out:$upgradeCoordinatorBinary" `
        /reference:System.dll `
        /reference:System.Core.dll `
        $upgradeCoordinatorSource
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $upgradeCoordinatorBinary))
    {
        throw "Upgrade coordinator compilation failed with exit code $LASTEXITCODE"
    }
    $selfTest = Start-Process `
        -FilePath $upgradeCoordinatorBinary `
        -ArgumentList @('--self-test', "`"$applicationBinary`"") `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0)
    {
        throw "Upgrade coordinator identity self-test failed with exit code $($selfTest.ExitCode)"
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
    if (-not $text.Contains('"ProductVersion" = "8:' + $Version + '"'))
    {
        throw "Installer ProductVersion does not match requested version $Version."
    }
    $replacements = [ordered]@{
        '"ProjectName" = "8:CodexQuotaPanelSetup"' = '"ProjectName" = "8:CodexQuotaPanelSetupEnUs"'
        '"LanguageId" = "3:2052"' = '"LanguageId" = "3:1033"'
        '"UILanguageId" = "3:2052"' = '"UILanguageId" = "3:1033"'
        "Debug\\CodexQuotaPanel-$Version-x64.msi" = "Debug-en-us\\CodexQuotaPanel-$Version-en-us-x64.msi"
        "Release\\CodexQuotaPanel-$Version-x64.msi" = "Release-en-us\\CodexQuotaPanel-$Version-en-us-x64.msi"
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
        "`"CodexQuotaPanelSetupEnUs`", `"CodexQuotaPanelSetup.en-us.v$Version.generated.vdproj`", `"{4DAF16CF-505F-4FEC-9F28-7FD152FB0732}`"")
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
        # Build a language-only target from the base MSI. A whole-database diff
        # between independent builds changes Media.Cabinet without transferring
        # its embedded CAB stream, causing error 2356 in English installations.
        Copy-Item -LiteralPath $baseMsi -Destination $localizedMsi -Force
        $englishDb = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $localizedMsi).Path, 1)
        $baseDb = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $baseMsi).Path, 0)
        $sourceDb = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $englishMsi).Path, 0)
        $exportDir = Join-Path $installerDir "$Configuration\language-tables-v$Version"
        New-Item -ItemType Directory -Path $exportDir -Force | Out-Null
        try {
            foreach ($table in @('ActionText','Error','UIText','Dialog','Control','ControlCondition',
                    'ControlEvent','EventMapping','TextStyle','RadioButton','ListBox','ListView','ComboBox',
                    'Billboard','BBControl','BBControlCondition','InstallUISequence','AdminUISequence')) {
                $exists = Get-MsiScalar $sourceDb "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name``='$table'"
                if (-not $exists) { continue }
                [void]$sourceDb.Export([string]$table, [string]$exportDir, [string]"$table.idt")
                if (Get-MsiScalar $englishDb "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name``='$table'") {
                    $drop = Invoke-ComMethod $englishDb 'OpenView' @("DROP TABLE ``$table``")
                    [void](Invoke-ComMethod $drop 'Execute')
                    [void](Invoke-ComMethod $drop 'Close')
                    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($drop)
                }
                [void]$englishDb.Import([string]$exportDir, [string]"$table.idt")
            }
            foreach ($property in @('ProductLanguage','ARPCOMMENTS')) {
                $value = Get-MsiScalar $sourceDb "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$property'"
                if ($null -eq $value) { continue }
                $escaped = $value.Replace("'", "''")
                $update = Invoke-ComMethod $englishDb 'OpenView' @("UPDATE ``Property`` SET ``Value``='$escaped' WHERE ``Property``='$property'")
                [void](Invoke-ComMethod $update 'Execute')
                [void](Invoke-ComMethod $update 'Close')
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($update)
            }
            [void](Invoke-ComMethod $englishDb 'Commit')
        }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($sourceDb) }
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

        # Fail packaging when a transform points to a missing CAB, even if all
        # translated dialogs look correct. Keep payload tables identical too.
        $baseDatabase = Invoke-ComMethod $installer 'OpenDatabase' @((Resolve-Path -LiteralPath $baseMsi).Path, 0)
        try {
            foreach ($table in @('Media','File','Component','FeatureComponents','MsiFileHash','Directory')) {
                $before = @(Get-MsiRows $baseDatabase $table)
                $after = @(Get-MsiRows $database $table)
                if (($before -join "`n") -cne ($after -join "`n")) { throw "Language transform changed payload table $table" }
            }
            $media = Invoke-ComMethod $database 'OpenView' @('SELECT `Cabinet` FROM `Media`')
            [void](Invoke-ComMethod $media 'Execute')
            while ($record = Invoke-ComMethod $media 'Fetch') {
                $cab = $record.StringData(1)
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
                if ($cab.StartsWith('#')) {
                    $name = $cab.Substring(1).Replace("'", "''")
                    if (-not (Get-MsiScalar $database "SELECT ``Name`` FROM ``_Streams`` WHERE ``Name``='$name'")) {
                        throw "Language transform refers to missing embedded CAB $cab"
                    }
                }
            }
            [void](Invoke-ComMethod $media 'Close')
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($media)
        }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($baseDatabase) }

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

function Get-MsiRows($Database, [string]$Table) {
    if (-not (Get-MsiScalar $Database "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name``='$Table'")) { return }
    $view = Invoke-ComMethod $Database 'OpenView' @("SELECT * FROM ``$Table``")
    try {
        [void](Invoke-ComMethod $view 'Execute')
        $rows = @()
        while ($record = Invoke-ComMethod $view 'Fetch') {
            $count = $record.GetType().InvokeMember('FieldCount', [Reflection.BindingFlags]::GetProperty, $null, $record, $null)
            $values = for ($i=1; $i -le $count; $i++) { $record.StringData($i) }
            $rows += ConvertTo-Json -InputObject @($values) -Compress
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        $rows | Sort-Object
    }
    finally {
        [void](Invoke-ComMethod $view 'Close')
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

if (-not $ReuseBuiltMsi) {
    $resolvedDevenv = (Resolve-Path -LiteralPath $DevenvPath).Path
    Build-UpgradeCoordinator
    New-EnglishProject
    Invoke-InstallerBuild $solutionPath
    & $postProcessor -MsiPath $baseMsi -IconPath $iconPath -UpgradeCoordinatorPath $upgradeCoordinatorBinary
    Invoke-InstallerBuild $generatedSolution
    & $postProcessor -MsiPath $englishMsi -IconPath $iconPath -UpgradeCoordinatorPath $upgradeCoordinatorBinary
}

New-EnglishTransform
Test-EnglishTransform
Write-Output "PASS localized installer v$Version | zh-CN base + en-US transform | $transformPath"
