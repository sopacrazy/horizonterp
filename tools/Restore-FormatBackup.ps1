param(
    [Parameter(Mandatory=$true)][string]$BackupFolder,
    [string]$Destination = 'C:\SistemaAdr\gta'
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $BackupFolder 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($item in $manifest.archives) {
    $archivePath = Join-Path $BackupFolder $item.file
    if ($item.parts) {
        $joined = [IO.File]::Create($archivePath)
        try {
            foreach ($part in $item.parts) {
                $partPath = Join-Path $BackupFolder $part.file
                if ((Get-Item -LiteralPath $partPath).Length -ne $part.bytes -or
                    (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash -ne $part.sha256) {
                    throw "Parte incompleta: $($part.file)"
                }
                $inputFile = [IO.File]::OpenRead($partPath)
                try { $inputFile.CopyTo($joined) } finally { $inputFile.Dispose() }
            }
        } finally { $joined.Dispose() }
    }
    $info = Get-Item -LiteralPath $archivePath
    if ($info.Length -ne $item.bytes) { throw "Tamanho incorreto: $($item.file)" }
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($hash -ne $item.sha256) { throw "SHA-256 incorreto: $($item.file)" }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $destinationRoot $entry.FullName))
            if (!$target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Caminho fora do projeto: $($entry.FullName)"
            }
        }
    } finally { $archive.Dispose() }
    Write-Host "Verificado; extraindo $($item.file)"
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $Destination, $true)
}
Write-Host 'Fontes restauradas. Instale Unity 6000.6.7f1 e abra a pasta TestePersonagem pelo Hub.'
Write-Host 'Restaure as chaves privadas separadamente a partir do HD. Nao publique essas chaves.'
