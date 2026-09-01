$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'GarlicSaveMgr\GarlicSaveMgr.csproj'
$publish = Join-Path $root 'publish'

Write-Host '== Garlic SaveMgr C# v6.8 =='
Write-Host 'Limpiando...'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host 'Restaurando...'
dotnet restore $project

Write-Host 'Compilando...'
dotnet build $project -c Release --no-restore

Write-Host 'Publicando win-x64 self-contained, single-file...'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o $publish

$exe = Join-Path $publish 'Garlic_SaveMgr.exe'
if (!(Test-Path $exe)) { throw "No se generó $exe" }
Write-Host "OK: $exe"
