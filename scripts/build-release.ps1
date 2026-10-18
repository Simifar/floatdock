param(
    [string]$DotnetPath = 'dotnet',
    # Optional Authenticode signing of FloatDock's own binaries (see scripts/sign.ps1); without it the release is unsigned.
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [System.IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$output = [System.IO.Path]::GetFullPath((Join-Path $artifacts 'FloatDock-win-x64'))
if (-not $output.StartsWith($artifacts + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output escaped the artifacts directory.'
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

& $DotnetPath publish (Join-Path $repository 'src/FloatDock.App/FloatDock.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'FloatDock.App publish failed.' }

# Guardian is compiled ahead of time (a few MB, no runtime of its own). The native link step finds the C++ tools through vswhere,
# which is not always on PATH.
$installer = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer'
if ((Test-Path -LiteralPath $installer) -and -not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) { $env:PATH = "$installer;$env:PATH" }
$guardian = Join-Path $output 'Guardian'
& $DotnetPath publish (Join-Path $repository 'src/FloatDock.Guardian/FloatDock.Guardian.csproj') `
    -c Release -r win-x64 -p:DebugType=none -p:DebugSymbols=false -o $guardian
if ($LASTEXITCODE -ne 0) { throw 'FloatDock.Guardian publish failed.' }
# The native linker writes a 12 MB symbols file next to the executable whatever DebugType says.
Get-ChildItem -LiteralPath $guardian -Filter '*.pdb' | Remove-Item -Force

foreach ($name in @('App.xbf', 'DockWindow.xbf', 'FloatDock.App.pri', 'FloatDock.App.exe', 'Guardian/FloatDock.Guardian.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $name))) { throw "Missing release file: $name" }
}

if ($CertificateThumbprint) {
    $own = @(Get-ChildItem -LiteralPath $output -Filter 'FloatDock*.exe') + @(Get-ChildItem -LiteralPath $output -Filter 'FloatDock*.dll') + @(Get-ChildItem -LiteralPath $guardian -Filter 'FloatDock*.exe')
    & (Join-Path $PSScriptRoot 'sign.ps1') -Path ($own | ForEach-Object FullName) -CertificateThumbprint $CertificateThumbprint -TimestampUrl $TimestampUrl
}

$zip = Join-Path $artifacts 'FloatDock-win-x64.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
Write-Output $zip
