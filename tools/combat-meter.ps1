param([switch]$Build)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'CombatMeter\CombatMeter.csproj'
$exe = Join-Path $PSScriptRoot 'CombatMeter\bin\Release\net10.0-windows\D3Offline.CombatMeter.exe'
if ($Build -or -not (Test-Path -LiteralPath $exe)) {
    & dotnet build $project -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Combat meter build failed.' }
}
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
