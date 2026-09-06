param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Security','Trash','Salud','Updater')]
    [string]$Module
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root ("Modules\$Module\$Module" + "Module.csproj")
if (-not (Test-Path $project)) { throw "No existe el proyecto del módulo: $project" }

Write-Host "== Garlic SaveMgr module: $Module =="
Write-Host "La compilación del módulo requiere que el host ya haya sido compilado al menos una vez."
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore $Module ha fallado." }
dotnet build $project -c Release --no-restore --no-dependencies
if ($LASTEXITCODE -ne 0) { throw "dotnet build $Module ha fallado." }
Write-Host "OK: $Module compilado."
