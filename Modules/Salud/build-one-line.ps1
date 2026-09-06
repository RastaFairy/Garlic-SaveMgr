param(
    [string]$HostProjectPath,
    [string]$HostAssemblyPath
)

$ErrorActionPreference = 'Stop'
$moduleDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $moduleDir 'SaludModule.csproj'

function Resolve-ExistingPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return (Resolve-Path -LiteralPath $Path).Path
        }
    } catch { }
    return $null
}

function Find-Upward([string]$StartDir, [string[]]$RelativeCandidates) {
    $current = [System.IO.DirectoryInfo]::new((Resolve-Path -LiteralPath $StartDir).Path)
    while ($null -ne $current) {
        foreach ($relative in $RelativeCandidates) {
            $candidate = Join-Path $current.FullName $relative
            $resolved = Resolve-ExistingPath $candidate
            if ($resolved) { return $resolved }
        }
        $current = $current.Parent
    }
    return $null
}

function Find-NearbyHostProject([string]$StartDir) {
    # First try common layouts while walking up from Modules\Salud.
    $direct = Find-Upward $StartDir @(
        'GarlicSaveMgr\GarlicSaveMgr.csproj',
        'src\GarlicSaveMgr\GarlicSaveMgr.csproj',
        'Garlic SaveMgr\GarlicSaveMgr.csproj'
    )
    if ($direct) { return $direct }

    # Then search the nearby project tree. Limit traversal depth to avoid wandering
    # through unrelated directories such as user profiles or whole disks.
    $root = (Resolve-Path -LiteralPath (Join-Path $StartDir '..\..')).Path
    try {
        $match = Get-ChildItem -LiteralPath $root -Filter 'GarlicSaveMgr.csproj' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch '[\\/]bin[\\/]' -and
                $_.FullName -notmatch '[\\/]obj[\\/]'
            } |
            Select-Object -First 1
        if ($match) { return $match.FullName }
    } catch { }

    return $null
}

function Find-NearbyHostAssembly([string]$StartDir) {
    $direct = Find-Upward $StartDir @(
        'GarlicSaveMgr\bin\Release\net8.0-windows\Garlic_SaveMgr.dll',
        'GarlicSaveMgr\bin\Debug\net8.0-windows\Garlic_SaveMgr.dll',
        'Garlic SaveMgr\bin\Release\net8.0-windows\Garlic_SaveMgr.dll',
        'Garlic SaveMgr\bin\Debug\net8.0-windows\Garlic_SaveMgr.dll'
    )
    if ($direct) { return $direct }

    $root = (Resolve-Path -LiteralPath (Join-Path $StartDir '..\..')).Path
    try {
        $match = Get-ChildItem -LiteralPath $root -Filter 'Garlic_SaveMgr.dll' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch '[\\/]obj[\\/]'
            } |
            Sort-Object FullName |
            Select-Object -First 1
        if ($match) { return $match.FullName }
    } catch { }

    return $null
}

Write-Host 'Compilando GarlicSaveMgr.SaludModule...' -ForegroundColor Cyan
Write-Host "Proyecto: $project"

$hostProject = Resolve-ExistingPath $HostProjectPath
if (-not $hostProject) { $hostProject = Find-NearbyHostProject $moduleDir }

$hostDll = Resolve-ExistingPath $HostAssemblyPath
if (-not $hostDll) { $hostDll = Find-NearbyHostAssembly $moduleDir }

$buildArgs = @($project, '-c', 'Release', '--nologo')
if ($hostProject) {
    Write-Host "Host detectado: $hostProject" -ForegroundColor Green
    $buildArgs += "/p:HostProjectPath=$hostProject"
} elseif ($hostDll) {
    Write-Host "Host compilado detectado: $hostDll" -ForegroundColor Yellow
    $buildArgs += "/p:HostAssemblyPath=$hostDll"
} else {
    Write-Error @"
No se ha encontrado el contrato/ensamblado del host.
Se buscó desde: $moduleDir
Prueba primero:
  .\build-one-line.ps1 -HostProjectPath 'C:\ruta\real\GarlicSaveMgr.csproj'
  .\build-one-line.ps1 -HostAssemblyPath 'C:\ruta\real\Garlic_SaveMgr.dll'
"@
    exit 2
}

dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build falló con código $LASTEXITCODE." }
Write-Host 'Build OK.' -ForegroundColor Green
