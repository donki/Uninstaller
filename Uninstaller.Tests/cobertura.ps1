# Pruebas con cobertura (constitucion General 8.6). Uso, desde cualquier carpeta:
#   pwsh Uninstaller.Tests/cobertura.ps1
# Compila y pasa los dos bancos con coverlet: Uninstaller.Tests (net10.0: logica, paginas, Shell,
# lanzador) y Uninstaller.WindowsTests (net10.0-windows: el codigo de Platforms\Windows). Resume con
# ReportGenerator (dotnet tool local del repo) y da las dos cifras: cobertura de lo instrumentado y
# cobertura de toda la app, que cuenta solo codigo ejecutable (General 8.6, 2026-11-25) con el script
# comun Mobile/Shared/cobertura-app.py; lo que ningun banco compila (Android) entra estimado.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$benches = @(
    @{ Project = Join-Path $repo 'Uninstaller.Tests'; Defines = '' },
    @{ Project = Join-Path $repo 'Uninstaller.WindowsTests'; Defines = 'WINDOWS' }
)
$code = 0
$watch = [Diagnostics.Stopwatch]::StartNew()
foreach ($b in $benches) {
    $results = Join-Path $b.Project 'TestResults'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }
    dotnet build $b.Project -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v:q -nologo | Out-Host
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
$watch.Reset()
foreach ($b in $benches) {
    $watch.Start()
    dotnet test $b.Project --no-build -s (Join-Path $b.Project coverlet.runsettings) --collect:"XPlat Code Coverage" --results-directory (Join-Path $b.Project 'TestResults') -nologo | Out-Host
    $watch.Stop()
    if ($LASTEXITCODE) { $code = $LASTEXITCODE }
    $b.Xml = (Get-ChildItem (Join-Path $b.Project 'TestResults') -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1).FullName
}
"Tiempo de los bancos de pruebas (dotnet test --no-build): {0:N1} s" -f $watch.Elapsed.TotalSeconds

$reports = ($benches | ForEach-Object { $_.Xml }) -join ';'
$out = Join-Path $PSScriptRoot 'TestResults/report'
Push-Location $repo
try {
    dotnet tool restore | Out-Null
    dotnet reportgenerator "-reports:$reports" "-targetdir:$out" -reporttypes:TextSummary | Out-Null
} finally { Pop-Location }
Get-Content "$out/Summary.txt" | Select-Object -First 12

$pyArgs = @('--app', $repo)
foreach ($b in $benches) { $pyArgs += @('--report', $b.Xml, '--defines', $b.Defines) }
python (Join-Path $repo '..\Shared\cobertura-app.py') @pyArgs
exit $code
