[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.6.0',
    [ValidateSet('osx-arm64','osx-x64')][string]$Runtime = 'osx-arm64',
    [string]$OutputDirectory,
    [switch]$SkipChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'macOS packaging must run on macOS.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$solution = Join-Path $repositoryRoot 'CodexQuotaPanel.VNext.slnx'
$aggregate = Join-Path $repositoryRoot 'build/CodexQuota.ReleaseAggregate.csproj'
$appProject = Join-Path $repositoryRoot 'src\CodexQuota.App\CodexQuota.App.csproj'
$stage = Join-Path $repositoryRoot "artifacts/release-stage/$Runtime"
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {
    Join-Path $repositoryRoot "artifacts/release-v$Version-macos"
}
$app = Join-Path $output 'CodexQuotaPanel.app'
$dmgStage = Join-Path $repositoryRoot "artifacts/release-dmg-stage-$Runtime"

function Reset-LocalDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $repositoryRoot.TrimEnd('/') + '/'
    if (-not $full.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "Unsafe path: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    New-Item -ItemType Directory -Path $full -Force | Out-Null
}

Reset-LocalDirectory $stage
Reset-LocalDirectory $output
Reset-LocalDirectory $dmgStage
dotnet restore $aggregate -r $Runtime -p:SelfContained=true
if ($LASTEXITCODE) { throw 'Restore failed.' }
dotnet build $aggregate -c Release -r $Runtime --self-contained true --no-restore `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE) { throw 'Build failed.' }
if (-not $SkipChecks) {
    foreach ($test in @('Domain','Application','Infrastructure','Platform')) {
        dotnet run --project (Join-Path $repositoryRoot "tests/CodexQuota.$test.Tests") -c Release -r $Runtime --no-build --no-restore
        if ($LASTEXITCODE) { throw "$test checks failed." }
    }
    dotnet run --project (Join-Path $repositoryRoot 'tests/CodexQuota.UI.Tests') -c Release -r $Runtime --no-build --no-restore -- `
        (Join-Path $repositoryRoot 'artifacts/vnext-preview') formal
    if ($LASTEXITCODE) { throw 'UI checks failed.' }
}
dotnet publish $appProject -c Release -r $Runtime --self-contained true --no-build --no-restore `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE) { throw 'Publish failed.' }

$macos = Join-Path $app 'Contents/MacOS'
$resources = Join-Path $app 'Contents/Resources'
New-Item -ItemType Directory -Path $macos,$resources -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $stage 'CodexQuotaPanel') -Destination (Join-Path $macos 'CodexQuotaPanel')
& chmod +x (Join-Path $macos 'CodexQuotaPanel')
$sourceIcon = Join-Path $repositoryRoot 'src/CodexQuota.App/Assets/CodexQuotaPanel.ico'
$iconPng = Join-Path $resources 'CodexQuotaPanel.png'
& sips -s format png $sourceIcon --out $iconPng | Out-Null
if ($LASTEXITCODE) { throw 'Icon conversion failed.' }
$iconSet = Join-Path $output 'CodexQuotaPanel.iconset'
New-Item -ItemType Directory -Path $iconSet -Force | Out-Null
foreach ($size in @(16,32,128,256,512)) {
    & sips -z $size $size $iconPng --out (Join-Path $iconSet "icon_${size}x${size}.png") | Out-Null
    $double = $size * 2
    & sips -z $double $double $iconPng --out (Join-Path $iconSet "icon_${size}x${size}@2x.png") | Out-Null
}
& iconutil -c icns $iconSet -o (Join-Path $resources 'CodexQuotaPanel.icns')
if ($LASTEXITCODE) { throw 'ICNS creation failed.' }
Remove-Item -LiteralPath $iconSet -Recurse -Force
$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>CodexQuotaPanel</string>
<key>CFBundleDisplayName</key><string>CodexQuotaPanel</string>
<key>CFBundleIdentifier</key><string>io.github.yaozhihang2002.codexquotapanel</string>
<key>CFBundleVersion</key><string>$Version</string>
<key>CFBundleShortVersionString</key><string>$Version</string>
<key>CFBundleExecutable</key><string>CodexQuotaPanel</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>CodexQuotaPanel</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
[IO.File]::WriteAllText((Join-Path $app 'Contents/Info.plist'), $plist, [Text.UTF8Encoding]::new($false))
& plutil -lint (Join-Path $app 'Contents/Info.plist')
if ($LASTEXITCODE) { throw 'Info.plist validation failed.' }

$identity = $env:CODE_SIGN_IDENTITY
if ([string]::IsNullOrWhiteSpace($identity)) { $identity = '-' }
& codesign --force --deep --options runtime --sign $identity $app
if ($LASTEXITCODE) { throw 'Code signing failed.' }
& codesign --verify --deep --strict --verbose=2 $app
if ($LASTEXITCODE) { throw 'Code signature verification failed.' }

$zip = Join-Path $output "CodexQuotaPanel-$Version-$Runtime.zip"
& ditto -c -k --sequesterRsrc --keepParent $app $zip
if ($LASTEXITCODE) { throw 'ZIP packaging failed.' }
$dmg = Join-Path $output "CodexQuotaPanel-$Version-$Runtime.dmg"
Copy-Item -LiteralPath $app -Destination (Join-Path $dmgStage 'CodexQuotaPanel.app') -Recurse
& /bin/ln -s /Applications (Join-Path $dmgStage 'Applications')
& hdiutil create -volname CodexQuotaPanel -srcfolder $dmgStage -ov -format UDZO $dmg
if ($LASTEXITCODE) { throw 'DMG packaging failed.' }
& /usr/bin/unzip -t $zip | Out-Null
if ($LASTEXITCODE) { throw 'ZIP integrity validation failed.' }
& hdiutil verify $dmg | Out-Null
if ($LASTEXITCODE) { throw 'DMG integrity validation failed.' }
$mount = Join-Path $repositoryRoot "artifacts/release-dmg-mount-$Runtime"
if (Test-Path -LiteralPath $mount) { Remove-Item -LiteralPath $mount -Recurse -Force }
New-Item -ItemType Directory -Path $mount -Force | Out-Null
try {
    & hdiutil attach -nobrowse -readonly -mountpoint $mount $dmg | Out-Null
    if ($LASTEXITCODE) { throw 'DMG mount validation failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $mount 'CodexQuotaPanel.app/Contents/MacOS/CodexQuotaPanel'))) {
        throw 'DMG application payload is missing.'
    }
    & /bin/test -L (Join-Path $mount 'Applications')
    if ($LASTEXITCODE) { throw 'DMG Applications shortcut is missing.' }
}
finally {
    & hdiutil detach $mount -quiet 2>$null
    Remove-Item -LiteralPath $mount -Recurse -Force -ErrorAction SilentlyContinue
}
$hashes = Get-ChildItem -LiteralPath $output -File | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $output "SHA256SUMS-v$Version-$Runtime.txt"), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output "PASS macOS package $Version $Runtime identity=$identity"
Write-Output "OUTPUT $output"
