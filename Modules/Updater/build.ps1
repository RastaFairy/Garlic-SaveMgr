$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'UpdaterModule.csproj'
$out = Join-Path $root 'bin\Release\net8.0-windows'

Write-Host '== Garlic SaveMgr UpdaterModule v1.0.0 =='
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore UpdaterModule ha fallado.' }
dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build UpdaterModule ha fallado.' }

$dll = Join-Path $out 'GarlicSaveMgr.UpdaterModule.dll'
if (!(Test-Path $dll)) { throw "No se generó $dll" }
Write-Host "OK: $dll"
