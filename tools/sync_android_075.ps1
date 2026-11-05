$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'TestePersonagem'
$target = Join-Path $root 'TestePersonagemAndroidLS'
if (!(Test-Path -LiteralPath "$target/Assets/Scenes/AndroidLosSantos/Entrada.unity")) { throw 'Perfil Android existente ausente.' }

# Refresh the recovered Los Santos build without scanning the entire PC map.
$folders = @('Assets/Scripts','Assets/Editor','Assets/Resources','Assets/Startup','Assets/Branding',
    'Assets/Prefabs','Assets/Animations','Assets/Plugins','Assets/ByBeyby','Assets/GTASanAndreasModFull/GroundRepairs',
    'Assets/GTASanAndreasModFull/CollisionRepairs','Packages','ProjectSettings')
foreach ($folder in $folders) {
    $from = Join-Path $source $folder
    $to = Join-Path $target $folder
    if (!(Test-Path -LiteralPath $from -PathType Container)) { continue }
    Write-Output "SYNC $folder"
    & robocopy.exe $from $to /E /COPY:DAT /R:1 /W:1 /MT:8 /NFL /NDL /NJH /NJS /NP /XJ
    if ($LASTEXITCODE -ge 8) { throw "Falha de cópia em $folder (robocopy $LASTEXITCODE)" }
    if (Test-Path -LiteralPath "$from.meta") { Copy-Item -LiteralPath "$from.meta" -Destination "$to.meta" -Force }
}
foreach ($name in @('Entrada.unity','TestePersonagem.unity','SanAndreasIntegration.enabled')) {
    foreach ($suffix in @('','.meta')) {
        $file = "$name$suffix"
        if (Test-Path -LiteralPath "$source/Assets/Scenes/$file") {
            Copy-Item -LiteralPath "$source/Assets/Scenes/$file" -Destination "$target/Assets/Scenes/$file" -Force
        }
    }
}
$relative = 'Assets/Scenes/GTASanAndreasModFullSectors'
$sectors = @(Get-ChildItem -LiteralPath "$target/$relative" -Filter '*.unity' -File)
if ($sectors.Count -ne 146) { throw "Esperados 146 setores Android, encontrados $($sectors.Count)." }
foreach ($sector in $sectors) {
    foreach ($suffix in @('','.meta')) {
        $file = "$($sector.Name)$suffix"
        Copy-Item -LiteralPath "$source/$relative/$file" -Destination "$target/$relative/$file" -Force
    }
}
$version = Get-Content -LiteralPath "$target/Assets/Resources/GameVersion.json" -Raw | ConvertFrom-Json
if ($version.versionCode -ne 24) { throw 'Configuração de versão não copiada.' }
$report = [ordered]@{source=$source; project=$target; sectors=$sectors.Count; version=$version.version; fullSourcePreserved=$true}
$folder = Join-Path $PSScriptRoot 'android-los-santos'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$report | ConvertTo-Json | Set-Content -LiteralPath "$folder/isolated-project.json" -Encoding utf8
$report | ConvertTo-Json
