param(
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'TestePersonagem'
$responses = Get-ChildItem -LiteralPath (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse |
    Sort-Object LastWriteTime -Descending
if (!$responses) { throw 'Compile o projeto no Unity uma vez para gerar as referências de C#.' }
$compiler = Get-ChildItem -LiteralPath (Join-Path $EditorData 'DotNetSdk/sdk') -Filter 'csc.dll' -Recurse |
    Select-Object -First 1
if (!$compiler) { throw 'Compilador C# do Unity não encontrado.' }

$output = Join-Path $workspace 'tools/criminal-economy-validation'
[IO.Directory]::CreateDirectory($output) | Out-Null
$responsePath = Join-Path $output 'Assembly-CSharp.rsp'
$assemblyPath = (Join-Path $output 'Assembly-CSharp.dll').Replace('\', '/')
$referencePath = (Join-Path $output 'Assembly-CSharp.ref.dll').Replace('\', '/')
$lines = @(Get-Content -LiteralPath $responses[0].FullName | ForEach-Object {
    if ($_ -match '^[-/]out:') { '-out:"' + $assemblyPath + '"' }
    elseif ($_ -match '^[-/]refout:') { '-refout:"' + $referencePath + '"' }
    else { $_ }
})
$economySources = @(
    'Assets/Scripts/AccountCriminalEconomy.cs',
    'Assets/Scripts/CharacterSkinSizing.cs',
    'Assets/Scripts/CriminalEconomyPilotPanel.cs',
    'Assets/Scripts/CriminalEconomyWorld.cs',
    'Assets/Scripts/GameHudEconomyMarkers.cs'
)
foreach ($economySource in $economySources) {
    if ((Test-Path -LiteralPath (Join-Path $project $economySource)) -and
        !($lines | Where-Object { $_.Trim('"') -eq $economySource })) {
        $lines += '"' + $economySource + '"'
    }
}
[IO.File]::WriteAllLines($responsePath, $lines, [Text.UTF8Encoding]::new($false))

Push-Location $project
try {
    # Compile against the existing Unity references without importing assets,
    # changing Library assemblies, running the game or producing a player build.
    & (Join-Path $EditorData 'DotNetSdk/dotnet.exe') $compiler.FullName ('@' + $responsePath)
    if ($LASTEXITCODE -ne 0) { throw "Validação C# falhou: código $LASTEXITCODE." }
    Write-Output 'CRIMINAL_ECONOMY_CSHARP_OK (compilação estática; Play e USS ainda precisam de revisão no Unity)'
}
finally { Pop-Location }
