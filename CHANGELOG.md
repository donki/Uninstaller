# Changelog

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.N` (constitucion 11).

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
