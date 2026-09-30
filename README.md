# Uninstaller

Desinstalador masivo de aplicaciones para **Android y Windows**, en .NET MAUI (el mismo proyecto
para los dos). Lista las apps instaladas, permite seleccionar varias con checkbox y desinstalarlas
en secuencia. Cumple la Constitución de Proyectos de Software de Socratic.

## Dónde conseguirla

- **Google Play:** https://play.google.com/store/apps/details?id=com.socratic.uninstaller
- **Microsoft Store:** «sOC Uninstaller» (en cuanto Partner Center dé el enlace).
- **Releases de GitHub** (APK / EXE / MSIX de cada versión): https://github.com/donki/Uninstaller/releases

## Qué hace

- Lista las apps instaladas (icono, nombre y paquete) con el `PackageManager` de Android.
- Filtra por apps de usuario (por defecto) o todas, incluidas las del sistema.
- Selección múltiple y acción **«Desinstalar seleccionadas»**.
- Android **no** permite el borrado masivo silencioso: por cada app seleccionada se lanza el
  intent de desinstalación del sistema (`ACTION_UNINSTALL_PACKAGE`), que el usuario confirma.
  Al terminar, la lista se refresca para reflejar lo que quedó instalado.
- **En Windows** (`Platforms/Windows/AppInventoryService`): los programas Win32 salen de las claves
  `Uninstall` del registro (64 bits, 32 bits y por usuario: nombre, editor, versión, fecha, tamaño
  estimado, icono) y las apps de la Microsoft Store del `PackageManager`. «Apps del sistema» son los
  componentes de Windows. Desinstalar abre el desinstalador del fabricante (o `msiexec /X`) y espera a
  que su clave desaparezca; los paquetes MSIX se quitan con `RemovePackageAsync`, sin diálogo.
  También uno detrás de otro. Antes de empezar pregunta si hacerlo **desatendido** (sin preguntas)
  donde el instalador lo admite: Windows Installer, los instaladores más habituales, `QuietUninstallString` y
  apps de la Store. Al minimizar se va a la bandeja.
- **Espacio en disco** (Windows, `Pages/DiskUsagePage` + `Services/DiskScanner`): estilo TreeSize.
  Árbol de carpetas con tamaños, mapa de rectángulos (treemap «squarified», `Services/Treemap`),
  ficheros más grandes, por tipo, por antigüedad y duplicados
  (tamaño + SHA-256 parcial y entero); unidades locales, de red y rutas UNC; ver en el Explorador,
  copiar ruta, papelera (`SHFileOperation` con deshacer) y exportar a CSV. `--disk [ruta]` la abre
  directamente. En Android no se enseña (haría falta `MANAGE_EXTERNAL_STORAGE`).
- **Paquetes Windows** (`tools\publicar-windows.ps1 -Msix`): `sOCUninstaller.exe`, un solo fichero
  (lanzador que lleva la app WinUI comprimida dentro y la desempaqueta en
  `%LOCALAPPDATA%\sOCUninstaller\app\<versión>`, porque WinUI no admite el single-file de .NET),
  más el zip de la carpeta y el MSIX sin firmar para la Store.

## Arquitectura (constitución 5, 7)

- `Pages/`: `MainPage` (lista + selección) y `AboutPage`. Code-behind delgado que delega en
  servicios; sin ViewModels.
- `Services/`: `ILocalizationService`/`LocalizationService` (i18n es/en), `ISettingsService`,
  `IAppInventoryService`, `IToastService`, `UpdateService` (comprobación de versión).
- `Platforms/Android/`: `AppInventoryService` (PackageManager e intents) y `ToastService`.
- `Models/InstalledApp`, `Helpers/ServiceHelper`.
- `Resources/Styles/`: `Colors.xaml` + `Styles.xaml` (tokens claro/oscuro), fusionados en `App.xaml`.

## Pruebas

**168 pruebas** (xUnit), todas pasan · cobertura del código probado **98,7 %** de líneas (95,6 %
de ramas) · sobre toda la app **28,0 %** (1 051 de ~3 750 líneas; el resto es interfaz MAUI y
código de plataforma: PackageManager, registro, Explorador, bandeja) · el banco tarda **~0,15 s**
(≈ 4 s con la cobertura). Medido el 2026-09-30.

```powershell
dotnet test Uninstaller.Tests
# con cobertura (coverlet) y resumen (ReportGenerator, herramienta local del repo)
dotnet test Uninstaller.Tests -s Uninstaller.Tests/coverlet.runsettings --collect:"XPlat Code Coverage"
dotnet tool restore; dotnet tool run reportgenerator -reports:Uninstaller.Tests/TestResults/*/coverage.cobertura.xml -targetdir:Uninstaller.Tests/TestResults/report -reporttypes:TextSummary
```

Se prueban la detección del instalador y la orden desatendida (MSI, Inno Setup, NSIS, orden
silenciosa del registro), el icono y la fecha del registro, la papelera (rutas, dueño, fichas
`$I` de las versiones 1 y 2), las carpetas protegidas, el escáner de espacio (tamaños, recuento,
orden, enlaces, cancelación) y los duplicados, el mapa de rectángulos, el orden y la búsqueda de la
lista, los formatos de tamaño y fecha, los idiomas y los ajustes. Todo sobre carpetas temporales:
nada toca el registro, la papelera ni los programas de verdad.

## Permisos (constitución 6, A.3)

- `QUERY_ALL_PACKAGES`: única forma en Android 11+ de enumerar todas las apps para listarlas.
  Requiere justificación en Play Console (gestor/desinstalador de aplicaciones).
- `REQUEST_DELETE_PACKAGES`: lanzar el flujo de desinstalación (el usuario confirma cada app).
- `INTERNET`: solo para la comprobación de versión al arrancar.

## Compilar

```
dotnet build -c Release -f net9.0-android36.0 -p:RunAOTCompilation=false
```

## Versionado

Esquema de fecha `AAAA.MM.DD.N`. Versión actual: `2026.07.19.0` (`versionCode` 202607190).
Ver `CHANGELOG.md`.
