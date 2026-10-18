# Prints the release number kept in Directory.Build.props (the one place it is set).
$props = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
$version
