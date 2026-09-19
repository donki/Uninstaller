# Changelog

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.NN` (constitucion 11).

## 2026.09.19.03 — Espacio en disco (estilo TreeSize), en Windows

`versionCode`: 2026091903 · Windows `2026.9.19.3`

- **Espacio en disco**: botón nuevo en la cabecera (y entrada en el menú), solo en Windows. Se
  escanea una unidad, una carpeta o una ruta de red (`\\servidor\recurso`) y se ve el **árbol de
  carpetas** con lo que ocupa cada una (tamaño, porcentaje del padre, ficheros, carpetas, última
  modificación), desplegable por fila; los **ficheros más grandes**; el reparto **por tipo de
  fichero** y **por antigüedad**; y los **ficheros duplicados** (mismo tamaño y mismo hash: primero
  los primeros 64 KB y después el fichero entero), con lo que se recuperaría dejando una copia.
- Sobre lo elegido: **ver en el Explorador**, **copiar la ruta** y **enviar a la papelera** (con
  deshacer; el árbol se actualiza sin volver a escanear). **Exportar** la vista a CSV en Documentos.
- Escaneo en paralelo y cancelable, con progreso; los enlaces simbólicos y puntos de unión no se
  siguen; las carpetas sin permiso se marcan y se sigue. `sOCUninstaller.exe --disk [ruta]` abre la
  utilidad directamente (y escanea la ruta si se da).

## 2026.09.19.02 — Progreso al desinstalar

`versionCode`: 2026091902 · Windows `2026.9.19.2`

- Mientras se desinstala, atendido o desatendido, se ve el progreso: cuál va (n de N), su nombre y
  la barra. Antes, entre un desinstalador y el siguiente la pantalla se quedaba muda.

## 2026.09.19.01 — Windows: lo que ocupa cada aplicación

`versionCode`: 2026091901 · Windows `2026.9.19.1`

- En Windows, el tamaño sale también para las apps de la Microsoft Store y para los programas que
  no lo declaran en el registro: se mide lo que ocupa su carpeta de instalación (en paralelo, para
  no retrasar la lista). Ordenar por tamaño ya sirve para ver qué se lleva el disco.

## 2026.09.19.00 — Windows: un solo exe, modo desatendido y bandeja

`versionCode`: 2026091900 · Windows `2026.9.19.0`

- **Un solo exe** (`sOCUninstaller.exe`): WinUI no admite el «single file» de .NET (falla al
  activar Microsoft.UI.Xaml), así que un lanzador pequeño lleva la aplicación dentro comprimida, la
  desempaqueta en `%LOCALAPPDATA%\sOCUninstaller\app\<versión>` la primera vez (o cuando cambia la
  versión) y la arranca desde ahí. Lo genera `tools\publicar-windows.ps1`, junto con el MSIX.
- **Modo desatendido**: al confirmar, si alguno de los marcados lo admite, pregunta si hacerlo
  desatendido (sin preguntas) o con el asistente de cada uno. Sin preguntas van Windows Installer
  (`/qn /norestart`), Inno Setup (`/VERYSILENT`), NSIS (`/S`), los que traen `QuietUninstallString`
  y las apps de la Store; el resto abre su asistente igualmente. Windows puede pedir permiso de
  administrador de todas formas.
- **Al minimizar se va a la bandeja** (icono en el área de notificación): clic para volver, botón
  derecho para Abrir o Salir.

## 2026.09.18.00 — También para Windows

`versionCode`: 2026091800 · Windows `2026.9.18.0`

- **Versión para Windows con el mismo proyecto MAUI** (constitución, anexo A.1): lista los
  programas Win32 del registro (64 bits, 32 bits y por usuario) y las apps de la Microsoft Store,
  con icono, editor, versión, fecha y tamaño estimado; buscador, ordenación y selección múltiple
  como en Android. Desinstalar abre el desinstalador de cada programa uno detrás de otro (o
  `msiexec`), y las apps de la Store se quitan directamente. «Apps del sistema» son los componentes
  de Windows. Se entrega como EXE autocontenido y MSIX.
- El proyecto pasa a .NET 10 (`net10.0-android36.0` y `net10.0-windows10.0.19041.0`).
- La segunda línea de cada fila es el editor en Windows (en Android sigue siendo el paquete).

## 2026.08.28.1

`versionCode`: 202608281

- **Buscador** en la cabecera: filtra por nombre visible y por nombre de paquete, para no tener
  que recorrer un centenar de aplicaciones hasta dar con la que se busca (nota de autor del
  2026-08-25). El contador refleja lo que queda a la vista, y *Seleccionar todo* marca solo lo
  filtrado: marcar de golpe lo que el buscador esconde seria una trampa. El contador de
  seleccionadas sigue siendo el total, porque al desinstalar se desinstalan todas las marcadas.

## 2026.08.01.0

`versionCode`: 202608010

- **Cada fila muestra ya sus propiedades**: fecha de instalación, fecha de la última actualización
  y tamaño (los APK instalados, base y splits). Se leen sin ningún permiso nuevo: el tamaño total
  con datos y caché exigiría `PACKAGE_USAGE_STATS`.
- **Ordenación visible y ampliada**: el criterio activo se muestra junto al contador
  («19 aplicaciones · por Fecha de instalación») y el selector se abre también pulsando ese texto,
  no solo con el botón. Se añade **Tamaño** a los criterios y el activo sale marcado con ✓.
  Antes no se veía que la lista se pudiera ordenar (nota de autor del 2026-08-01).
- Los textos de ordenación estaban fijos en el código en castellano e inglés; ahora salen del
  diccionario de traducciones como el resto (constitución 8).
- `Resources\AppIcon\play_store_icon.png` regenerado desde los SVG actuales, que habían cambiado
  en el rediseño índigo del 28-jul.

## 2026.07.19.0

- Version inicial. App MAUI solo Android (`com.socratic.uninstaller`).
- Listado de aplicaciones instaladas con icono, nombre y paquete, via `PackageManager`.
- Filtro apps de usuario / todas (incluye sistema), persistido en preferencias.
- Seleccion multiple con checkbox y accion "Desinstalar seleccionadas" que lanza el
  intent del sistema por cada app en secuencia (el usuario confirma cada una).
- Refresco de la lista al terminar; manejo de errores con avisos localizados.
- Internacionalizacion es/en por diccionarios (`LocalizationService`).
- Menu hamburguesa (Shell Flyout) con Inicio y Acerca de.
- Pantalla "Acerca de" con las 7 tarjetas de la constitucion (A.9).
- Comprobacion de version al arrancar contra `appcast.json` (constitucion 15).
- Sistema de diseño (Colors.xaml + Styles.xaml, tokens claro/oscuro).
