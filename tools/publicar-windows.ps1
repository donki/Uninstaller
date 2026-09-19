<#
.SYNOPSIS
    Genera los paquetes Windows de sOC Uninstaller: el exe de un solo fichero y el MSIX de la Store.
.DESCRIPTION
    1. Publica la aplicacion MAUI/WinUI sin empaquetar (carpeta con Uninstaller.exe y sus ficheros).
    2. La comprime en Launcher\app.zip y compila el lanzador nativo (NativeAOT), que lleva ese zip
       dentro y lo desempaqueta en %LOCALAPPDATA%\sOCUninstaller\app\<version> al arrancar. WinUI no
       admite PublishSingleFile (falla al activar Microsoft.UI.Xaml), por eso el rodeo.
    3. Con -Msix, publica ademas el MSIX sin firmar para Partner Center.
    Todo sale en bin\windows\: sOCUninstaller.exe, sOCUninstaller-<version>.zip (la carpeta, por si
    alguien la quiere suelta) y sOCUninstaller-<version msix>.msix.
.EXAMPLE
    .\tools\publicar-windows.ps1 -Msix
#>
[CmdletBinding()]
param([switch] $Msix)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz "Uninstaller.csproj"
$tfm = "net10.0-windows10.0.19041.0"
$salida = Join-Path $raiz "bin\windows"
New-Item -ItemType Directory -Force $salida | Out-Null

# La version Windows sale de ApplicationDisplayVersion (2026.09.18.00 -> 2026.9.18.0).
$csproj = Get-Content $proyecto -Raw
if ($csproj -notmatch "<ApplicationDisplayVersion>([\d\.]+)</ApplicationDisplayVersion>") { throw "No se encuentra ApplicationDisplayVersion." }
$version = ($Matches[1].Split(".") | ForEach-Object { [int]$_ }) -join "."
"Version Windows: $version"

# 1. Publicacion sin empaquetar.
$publicado = Join-Path $raiz "bin\Release\$tfm\win-x64\publish"
if (Test-Path $publicado) { Remove-Item $publicado -Recurse -Force }
"Publicando la aplicacion..."
dotnet publish $proyecto -f $tfm -c Release -p:WindowsPackageType=None -p:PublishReadyToRun=false -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Ha fallado el publish de la aplicacion." }
Remove-Item (Join-Path $publicado "*.pdb") -Force -ErrorAction SilentlyContinue

# 2. Zip dentro del lanzador y lanzador nativo.
$launcher = Join-Path $raiz "Launcher"
$zip = Join-Path $launcher "app.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
"Comprimiendo..."
Get-ChildItem $publicado | Compress-Archive -DestinationPath $zip -CompressionLevel Optimal
Set-Content (Join-Path $launcher "version.txt") $version -NoNewline
Copy-Item $zip (Join-Path $salida "sOCUninstaller-$version.zip") -Force
"Compilando el lanzador..."
dotnet publish (Join-Path $launcher "Launcher.csproj") -c Release -r win-x64 "-p:LauncherVersion=$version" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Ha fallado el publish del lanzador." }
$exe = Get-ChildItem (Join-Path $launcher "bin\Release\net10.0\win-x64\publish") -Filter sOCUninstaller.exe | Select-Object -First 1
Copy-Item $exe.FullName (Join-Path $salida "sOCUninstaller.exe") -Force
Remove-Item $zip -Force
"Lanzador: $(Join-Path $salida 'sOCUninstaller.exe') ($([math]::Round($exe.Length/1MB,1)) MB)"

# 3. MSIX para la Store (sin firmar: lo firma Partner Center).
if ($Msix) {
    "Publicando el MSIX..."
    $paquetes = Join-Path $raiz "bin\Release\$tfm\win-x64\AppPackages"
    if (Test-Path $paquetes) { Remove-Item $paquetes -Recurse -Force }
    dotnet publish $proyecto -f $tfm -c Release -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "Ha fallado el publish del MSIX." }
    $paquete = Get-ChildItem $paquetes -Recurse -Filter *.msix | Select-Object -First 1
    $nombre = $paquete.Name -replace '^Uninstaller_([\d\.]+)_x64\.msix$', 'sOCUninstaller-$1.msix'
    Get-ChildItem $salida -Filter *.msix | Remove-Item -Force
    Copy-Item $paquete.FullName (Join-Path $salida $nombre) -Force
    "MSIX: $(Join-Path $salida $nombre)"
}
