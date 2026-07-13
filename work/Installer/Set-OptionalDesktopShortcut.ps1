param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,
    [Parameter(Mandatory = $true)]
    [string]$IconPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$resolvedIcon = (Resolve-Path -LiteralPath $IconPath).Path
$installer = $null
$database = $null

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

function Invoke-MsiNonQuery {
    param([Parameter(Mandatory = $true)][string]$Sql)

    $view = Invoke-ComMethod $database 'OpenView' @($Sql)
    try
    {
        [void](Invoke-ComMethod $view 'Execute')
    }
    finally
    {
        [void](Invoke-ComMethod $view 'Close')
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Get-MsiScalar {
    param([Parameter(Mandatory = $true)][string]$Sql)

    $view = Invoke-ComMethod $database 'OpenView' @($Sql)
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

function Set-MsiProperty {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ($null -eq (Get-MsiScalar "SELECT ``Property`` FROM ``Property`` WHERE ``Property``='$Name'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('$Name', '$Value')"
    }
    else
    {
        Invoke-MsiNonQuery "UPDATE ``Property`` SET ``Value``='$Value' WHERE ``Property``='$Name'"
    }
}

function Add-MsiIcon {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($null -ne (Get-MsiScalar "SELECT ``Name`` FROM ``Icon`` WHERE ``Name``='$Name'"))
    {
        Invoke-MsiNonQuery "DELETE FROM ``Icon`` WHERE ``Name``='$Name'"
    }

    $view = Invoke-ComMethod $database 'OpenView' @('INSERT INTO `Icon` (`Name`, `Data`) VALUES (?, ?)')
    $record = Invoke-ComMethod $installer 'CreateRecord' @(2)
    try
    {
        $record.GetType().InvokeMember(
            'StringData',
            [System.Reflection.BindingFlags]::SetProperty,
            $null,
            $record,
            @(1, $Name)) | Out-Null
        [void](Invoke-ComMethod $record 'SetStream' @(2, $Path))
        [void](Invoke-ComMethod $view 'Execute' @($record))
    }
    finally
    {
        [void](Invoke-ComMethod $view 'Close')
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

try
{
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-ComMethod $installer 'OpenDatabase' @($resolvedMsi, 1)

    $arpIconName = 'CodexQuotaPanel.ico'
    Add-MsiIcon $arpIconName $resolvedIcon
    if ($null -ne (Get-MsiScalar "SELECT ``Name`` FROM ``Icon`` WHERE ``Name``='CodexQuotaPanelIcon'"))
    {
        Invoke-MsiNonQuery "DELETE FROM ``Icon`` WHERE ``Name``='CodexQuotaPanelIcon'"
    }

    $shortcut = Get-MsiScalar "SELECT ``Shortcut`` FROM ``Shortcut`` WHERE ``Directory_``='DesktopFolder'"
    if ([string]::IsNullOrWhiteSpace($shortcut))
    {
        throw 'The generated MSI does not contain a desktop shortcut to make optional.'
    }

    # The setup project contains one payload file. Windows Installer SQL does
    # not support the LIKE pattern used by regular SQL engines, so validate the
    # filename first and then read the corresponding first-row keys directly.
    $applicationFileName = Get-MsiScalar "SELECT ``FileName`` FROM ``File``"
    if ([string]::IsNullOrWhiteSpace($applicationFileName) -or
        -not $applicationFileName.EndsWith('CodexQuotaPanel.exe', [StringComparison]::OrdinalIgnoreCase))
    {
        throw "The generated MSI does not contain the expected CodexQuotaPanel executable: $applicationFileName"
    }
    $applicationComponent = Get-MsiScalar "SELECT ``Component_`` FROM ``File``"
    $applicationFile = Get-MsiScalar "SELECT ``File`` FROM ``File``"
    if ([string]::IsNullOrWhiteSpace($applicationComponent) -or
        [string]::IsNullOrWhiteSpace($applicationFile))
    {
        throw 'The generated MSI does not contain the CodexQuotaPanel executable keys.'
    }
    $nonAdvertisedTarget = "[#$applicationFile]"

    $componentName = 'DesktopShortcutComponent'
    $componentGuid = '{31A7A62B-4302-4EF7-A5A1-2B8D61BAF905}'
    $registryName = 'DesktopShortcutPreference'

    if ($null -eq (Get-MsiScalar "SELECT ``Component`` FROM ``Component`` WHERE ``Component``='$componentName'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``Component`` (``Component``, ``ComponentId``, ``Directory_``, ``Attributes``, ``Condition``, ``KeyPath``) VALUES ('$componentName', '$componentGuid', 'DesktopFolder', 4, 'CREATEDESKTOPSHORTCUT=1', '$registryName')"
    }
    else
    {
        Invoke-MsiNonQuery "UPDATE ``Component`` SET ``Condition``='CREATEDESKTOPSHORTCUT=1', ``KeyPath``='$registryName', ``Attributes``=4 WHERE ``Component``='$componentName'"
    }

    if ($null -eq (Get-MsiScalar "SELECT ``Registry`` FROM ``Registry`` WHERE ``Registry``='$registryName'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``Registry`` (``Registry``, ``Root``, ``Key``, ``Name``, ``Value``, ``Component_``) VALUES ('$registryName', 1, 'Software\CodexQuotaPanel', 'DesktopShortcut', '#1', '$componentName')"
    }

    if ($null -eq (Get-MsiScalar "SELECT ``Component_`` FROM ``FeatureComponents`` WHERE ``Feature_``='DefaultFeature' AND ``Component_``='$componentName'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``FeatureComponents`` (``Feature_``, ``Component_``) VALUES ('DefaultFeature', '$componentName')"
    }

    Invoke-MsiNonQuery "UPDATE ``Shortcut`` SET ``Component_``='$componentName' WHERE ``Directory_``='DesktopFolder'"
    # Non-advertised shortcuts inherit the icon embedded in the installed EXE.
    # This avoids Windows Installer's advertised-shortcut rule that requires a
    # separate EXE-format icon stream whose extension matches the target.
    Invoke-MsiNonQuery "UPDATE ``Shortcut`` SET ``Target``='$nonAdvertisedTarget', ``Icon_``=NULL, ``IconIndex``=NULL"

    if ($null -eq (Get-MsiScalar "SELECT ``Property`` FROM ``Property`` WHERE ``Property``='CREATEDESKTOPSHORTCUT'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('CREATEDESKTOPSHORTCUT', '1')"
    }
    else
    {
        Invoke-MsiNonQuery "UPDATE ``Property`` SET ``Value``='1' WHERE ``Property``='CREATEDESKTOPSHORTCUT'"
    }

    if ($null -eq (Get-MsiScalar "SELECT ``Property`` FROM ``Property`` WHERE ``Property``='ARPPRODUCTICON'"))
    {
        Invoke-MsiNonQuery "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('ARPPRODUCTICON', '$arpIconName')"
    }
    else
    {
        Invoke-MsiNonQuery "UPDATE ``Property`` SET ``Value``='$arpIconName' WHERE ``Property``='ARPPRODUCTICON'"
    }

    # Visual Studio Installer sequences custom dialogs by returning to the
    # outer dialog loop. It does not always emit the reverse navigation
    # property for the following folder page, leaving its Back button with no
    # target. Wire the customized path explicitly in both directions.
    Set-MsiProperty 'CustomCheckA_NextArgs' 'FolderForm'
    Set-MsiProperty 'FolderForm_PrevArgs' 'CustomCheckA'
    Set-MsiProperty 'FolderForm_NextArgs' 'ConfirmInstallForm'
    Set-MsiProperty 'ConfirmInstallForm_PrevArgs' 'FolderForm'

    # The stock welcome dialog uses an aggressive copyright-prosecution
    # warning. This project is MIT licensed, so use a short, accurate open
    # source notice instead.
    $openSourceNotice = if ((Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductLanguage'") -eq '2052')
    {
        '{\VSI_MS_Sans_Serif13.0_0_0}CodexQuotaPanel 是依据 MIT 许可证发布的开源软件。'
    }
    else
    {
        '{\VSI_MS_Sans_Serif13.0_0_0}CodexQuotaPanel is open-source software released under the MIT License.'
    }
    Invoke-MsiNonQuery "UPDATE ``Control`` SET ``Text``='$openSourceNotice' WHERE ``Control``='CopyrightWarningText'"

    [void](Invoke-ComMethod $database 'Commit')

    $condition = Get-MsiScalar "SELECT ``Condition`` FROM ``Component`` WHERE ``Component``='$componentName'"
    $boundComponent = Get-MsiScalar "SELECT ``Component_`` FROM ``Shortcut`` WHERE ``Directory_``='DesktopFolder'"
    $shortcutTarget = Get-MsiScalar "SELECT ``Target`` FROM ``Shortcut`` WHERE ``Directory_``='DesktopFolder'"
    $shortcutIcon = Get-MsiScalar "SELECT ``Icon_`` FROM ``Shortcut`` WHERE ``Directory_``='DesktopFolder'"
    $arpIcon = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ARPPRODUCTICON'"
    $arpIconRow = Get-MsiScalar "SELECT ``Name`` FROM ``Icon`` WHERE ``Name``='$arpIconName'"
    $productLanguage = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductLanguage'"
    $afterOptionsDialog = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='CustomCheckA_NextArgs'"
    $beforeFolderDialog = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='FolderForm_PrevArgs'"
    $afterFolderDialog = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='FolderForm_NextArgs'"
    $beforeConfirmationDialog = Get-MsiScalar "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ConfirmInstallForm_PrevArgs'"
    $confirmationTitle = Get-MsiScalar "SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='ConfirmInstallForm' AND ``Control``='BannerText'"
    $welcomeNotice = Get-MsiScalar "SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='WelcomeForm' AND ``Control``='CopyrightWarningText'"
    $confirmationTitleValid = if ($productLanguage -eq '2052')
    {
        $confirmationTitle -like '*确认安装*'
    }
    else
    {
        $confirmationTitle -like '*Confirm Installation*'
    }
    if ($condition -ne 'CREATEDESKTOPSHORTCUT=1' -or
        $boundComponent -ne $componentName -or
        $shortcutTarget -ne $nonAdvertisedTarget -or
        -not [string]::IsNullOrEmpty($shortcutIcon) -or
        $arpIcon -ne $arpIconName -or
        $arpIconRow -ne $arpIconName -or
        $afterOptionsDialog -ne 'FolderForm' -or
        $beforeFolderDialog -ne 'CustomCheckA' -or
        $afterFolderDialog -ne 'ConfirmInstallForm' -or
        $beforeConfirmationDialog -ne 'FolderForm' -or
        -not $confirmationTitleValid -or
        $welcomeNotice -notlike '*MIT*')
    {
        throw "The installer MSI tables did not validate after commit: language=$productLanguage; condition=$condition; component=$boundComponent; target=$shortcutTarget; shortcutIcon=$shortcutIcon; arpIcon=$arpIcon; optionsNext=$afterOptionsDialog; folderBack=$beforeFolderDialog; folderNext=$afterFolderDialog; confirmBack=$beforeConfirmationDialog; confirmation=$confirmationTitle; notice=$welcomeNotice"
    }

    Write-Output "PASS installer customization | language=$productLanguage + optional desktop shortcut + bidirectional navigation + MIT notice + EXE logo + branded ARP icon + final confirmation | $resolvedMsi"
}
finally
{
    if ($null -ne $database)
    {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    }
    if ($null -ne $installer)
    {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
