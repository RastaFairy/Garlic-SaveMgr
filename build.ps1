$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'GarlicSaveMgr\GarlicSaveMgr.csproj'
$publish = Join-Path $root 'publish'
$version = '6.8.7.50'
$moduleProjects = @(
    (Join-Path $root 'Modules\Security\SecurityModule.csproj'),
    (Join-Path $root 'Modules\Trash\TrashModule.csproj'),
    (Join-Path $root 'Modules\Salud\SaludModule.csproj'),
    (Join-Path $root 'Modules\Updater\UpdaterModule.csproj')
)

Write-Host "== Garlic SaveMgr C# v$version - Salud 2.2 + Snapshots + Smart Backup + módulos autónomos =="
Write-Host 'Limpiando...'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
foreach ($moduleProject in $moduleProjects) {
    $moduleDir = Split-Path -Parent $moduleProject
    foreach ($name in @('bin','obj','publish')) {
        $path = Join-Path $moduleDir $name
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
}
$hostDir = Split-Path -Parent $project
foreach ($name in @('bin','obj')) {
    $path = Join-Path $hostDir $name
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
$testProject = Join-Path $root 'GarlicSaveMgr.Tests\GarlicSaveMgr.Tests.csproj'
$testDir = Split-Path -Parent $testProject
foreach ($name in @('bin','obj')) {
    $path = Join-Path $testDir $name
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host 'Restaurando Garlic SaveMgr...'
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore Garlic SaveMgr ha fallado.' }
Write-Host 'Compilando Garlic SaveMgr...'
dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build Garlic SaveMgr ha fallado.' }

foreach ($moduleProject in $moduleProjects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($moduleProject)
    Write-Host "Restaurando $name..."
    dotnet restore $moduleProject
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore $name ha fallado." }
    Write-Host "Compilando $name..."
    dotnet build $moduleProject -c Release --no-restore --no-dependencies
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $name ha fallado." }
}

Write-Host 'Restaurando pruebas...'
dotnet restore $testProject
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore pruebas ha fallado.' }
Write-Host 'Ejecutando pruebas de regresión...'
dotnet test $testProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de regresión han fallado.' }

Write-Host 'Publicando Garlic SaveMgr win-x64 self-contained, single-file...'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish Garlic SaveMgr ha fallado.' }
$exe = Join-Path $publish 'Garlic_SaveMgr.exe'
if (!(Test-Path $exe)) { throw "No se generó $exe" }

$moduleTargets = @{
    'Security' = 'GarlicSaveMgr.SecurityModule.dll'
    'Trash'    = 'GarlicSaveMgr.TrashModule.dll'
    'Salud'    = 'GarlicSaveMgr.SaludModule.dll'
    'Updater'  = 'GarlicSaveMgr.UpdaterModule.dll'
}
foreach ($entry in $moduleTargets.GetEnumerator()) {
    $source = Join-Path $root ("Modules\$($entry.Key)\bin\Release\net8.0-windows\$($entry.Value)")
    if (!(Test-Path $source)) { throw "No se generó el módulo $($entry.Key): $source" }
    $targetDir = Join-Path $publish ("Modules\$($entry.Key)")
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item $source (Join-Path $targetDir $entry.Value) -Force
    if (!(Test-Path (Join-Path $targetDir $entry.Value))) { throw "El módulo $($entry.Key) no llegó al publish final." }
}

Write-Host 'OK: host y cuatro módulos autónomos compilados/copiados.'
Write-Host "OK: $exe"
Write-Host 'Publicación completada.'
