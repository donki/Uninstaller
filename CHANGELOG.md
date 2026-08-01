# Changelog

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.N` (constitucion 11).

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
