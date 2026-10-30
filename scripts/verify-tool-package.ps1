param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$ToolDirectory
)

$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path $PackageDirectory).Path
New-Item -ItemType Directory -Path $ToolDirectory -Force | Out-Null
$toolPath = (Resolve-Path $ToolDirectory).Path
$configPath = Join-Path $toolPath 'nuget.config'
$escapedSource = [System.Security.SecurityElement]::Escape($packagePath)
Set-Content -Path $configPath -Value "<configuration><packageSources><clear /><add key='local' value='$escapedSource' /></packageSources></configuration>"

dotnet tool install --tool-path $toolPath --configfile $configPath Crap4CSharp.Tool --version 0.1.0-preview.1
if ($LASTEXITCODE -ne 0) { throw 'Tool installation failed' }

$commandName = if ($IsWindows) { 'crap4csharp.exe' } else { 'crap4csharp' }
$toolCommand = Join-Path $toolPath $commandName
& $toolCommand --help
if ($LASTEXITCODE -ne 0) { throw 'Tool help failed' }

$emptyWorkspace = Join-Path $toolPath 'smoke-empty'
New-Item -ItemType Directory -Path $emptyWorkspace -Force | Out-Null
Push-Location $emptyWorkspace
try {
    $json = & $toolCommand --format json
    if ($LASTEXITCODE -ne 0) { throw 'Tool JSON smoke failed' }
    $report = ($json -join "`n") | ConvertFrom-Json -AsHashtable
    if ($report['members'] -isnot [array] -or $report['members'].Count -ne 0) {
        throw 'Tool JSON report was not an empty members array'
    }
} finally {
    Pop-Location
}

Write-Output 'Packaged tool install, help, and JSON smoke passed.'
