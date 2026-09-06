param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Security','Trash','Salud','Updater')]
    [string]$Module
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root ("Modules\$Module\$Module" + 'Module.csproj')
if (!(Test-Path $project)) { throw "No existe el proyecto del módulo: $project" }

Write-Host "== Garlic SaveMgr módulo $Module =="
Write-Host 'Restaurando...'
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore $Module ha fallado." }
Write-Host 'Compilando solo el módulo...'
dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build $Module ha fallado." }
Write-Host "OK: $Module compilado."
