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
  donde el instalador lo admite: Windows Installer, Inno Setup, NSIS, `QuietUninstallString` y
  apps de la Store. Al minimizar se va a la bandeja.
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
