$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'GarlicSaveMgr.sln'
$project = Join-Path $root 'GarlicSaveMgr\GarlicSaveMgr.csproj'
$publish = Join-Path $root 'publish'

Write-Host '== Garlic SaveMgr C# v6.8 =='
Write-Host 'Comprobando .NET SDK...'
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'No se encontró el comando dotnet. Instala el SDK de .NET 8 antes de compilar.' }
Write-Host "SDK detectado: $dotnetVersion"

Write-Host 'Limpiando...'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'La restauración de NuGet ha fallado.' }

dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'La compilación de la solución ha fallado.' }

dotnet test (Join-Path $root 'GarlicSaveMgr.Tests\GarlicSaveMgr.Tests.csproj') -c Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) { throw 'Los tests han fallado.' }

Write-Host 'Publicando win-x64 self-contained, single-file...'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'La publicación win-x64 ha fallado.' }

$exe = Join-Path $publish 'Garlic_SaveMgr.exe'
if (!(Test-Path $exe)) { throw "No se generó $exe" }
Write-Host "OK: $exe"
