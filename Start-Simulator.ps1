param([switch]$Build)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 $exe=Join-Path $PSScriptRoot 'output/FluxoCaixa.Simulator.exe'
 if($Build -or !(Test-Path -LiteralPath $exe)) {
  dotnet publish FluxoCaixa.Simulator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o output
  if($LASTEXITCODE -ne 0){throw 'Falha ao compilar o simulador.'}
 }
 Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -WindowStyle Normal
}
finally {Pop-Location}
