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

- `Pages/`: `MainPage` (lista + selección), `DiskUsagePage` (espacio en disco, Windows),
  `SettingsPage` y `AboutPage`. Code-behind fino: vuelca en los controles el estado de su
  view-model y le pasa los toques.
- `ViewModels/`: la lógica de cada pantalla, en C# sin controles (se prueba en `Uninstaller.Tests`).
  Lo que solo existe en el dispositivo va detrás de interfaces: `IMainView`/`IDiskView` (lo que hace
  la página), `IDialogService` (diálogos), `IAppEnvironment` (versión, navegador, correo),
  `IDesktopIntegration` (bandeja y arranque con Windows).
- `Services/`: `ILocalizationService`/`LocalizationService` (i18n es/en), `ISettingsService`,
  `IAppInventoryService`, `IToastService`, `UpdateService` (comprobación de versión).
- `Platforms/Android/`: `AppInventoryService` (PackageManager e intents) y `ToastService`.
- `Models/InstalledApp`, `Helpers/ServiceHelper`.
- `Resources/Styles/`: `Colors.xaml` + `Styles.xaml` (tokens claro/oscuro), fusionados en `App.xaml`.

## Pruebas

**330 pruebas** (xUnit), todas pasan · cobertura del código probado **87,4 %** de líneas · sobre
toda la app **54,9 %** (2 918 de 5 312 líneas) · el banco tarda **~5 s** (≈ 10 s con el arranque de
`dotnet test` y la cobertura). Medido el 2026-11-24 (el 2026-10-01: 267 pruebas, 50,8 % de 4 180
líneas; antes de esta tanda, con el código de octubre, 47,5 % de 5 307).

Se prueba la lógica de las cuatro pantallas (`ViewModels/`: lista de aplicaciones, espacio en disco,
Ajustes, Acerca de) con dobles del inventario, del Explorador y de los diálogos, y sobre carpetas
temporales de verdad (escanear, duplicados, mapa, papelera, exportar); la lectura del registro
(`UninstallRegistry`), el escáner de disco, el mapa de rectángulos, las órdenes de desinstalar, la
papelera, las carpetas protegidas, la comprobación de versión (HTTP simulado) y las traducciones.
Desde el 2026-11-24 también **las páginas de verdad**: el proyecto de pruebas compila con MAUI los
XAML de las cuatro páginas, el Shell y la App, las crea con los dobles y pulsa sus controles
(botones, buscador, interruptores, desplegables y los diálogos, que se contestan pulsando su botón);
además el arranque (`MauiProgram`) y el lanzador de un solo exe (desempaquetar sin dejarlo a medias).
Queda sin probar lo que solo existe en el dispositivo: el código de Windows (registro, PackageManager,
Explorador, bandeja, barra de tareas, menús de WinUI y lo que va dentro de `#if WINDOWS` en las
páginas) y de Android (PackageManager). El plan para llegar al 90 % está en el fichero de tareas.
```powershell
dotnet test Uninstaller.Tests
pwsh Uninstaller.Tests/cobertura.ps1   # pruebas + las dos coberturas + tiempo
```

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
