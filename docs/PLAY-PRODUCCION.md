# Solicitud de acceso a producción en Google Play — sOC Uninstaller

Respuestas para el cuestionario de Play Console › **Panel › Solicitar acceso a producción**, en
catalán (el idioma de la consola). Cada texto cabe en los 300 caracteres del formulario; el número
entre paréntesis es su longitud. Constitución Mobile §11. **Última actualización: 2026-10-01**
(versión 2026.09.30.00). Estado en Play: prueba cerrada (alpha 2026.09.30.00 publicada; en la pista desde el 2026-09-12).

> Lo marcado con ⚠ no lo puedo saber yo: compruébalo en la consola antes de enviarlo y cámbialo si
> no es así.
>
> - ⚠ La API dice que la pista **production** tiene la 2026.08.01.0 «completed»: comprueba en el Panel si la app ya está en producción; si lo está, el cuestionario no hace falta.
> - ⚠ Usa `QUERY_ALL_PACKAGES`: la declaración de ese permiso (gestor/desinstalador de apps) tiene que estar aceptada en Contenido de la aplicación.

---

## Informació sobre la prova tancada

**Com has reclutat usuaris per a la prova tancada?** (259) ⚠ *comprueba que la probaste en el Xiaomi (el CHANGELOG no lo apunta).*

```
He afegit a la prova tancada quatre grups públics de Google de verificadors voluntaris (comunitats d'intercanvi de proves de 12 persones durant 14 dies). No he fet servir cap proveïdor de pagament. També l'he provada jo mateix en un mòbil real amb Android 16.
```

**Fins a quin punt t'ha resultat fàcil reclutar verificadors?** — Propuesta: **Ni fàcil ni difícil** (los grupos públicos dan el número, pero participan poco).

**Descriu la implicació dels verificadors** (258) ⚠ *comprueba en Estadísticas / Prova tancada que de verdad la abrieron; si no hay datos, quita la parte de las funciones.*

```
Els verificadors han instal·lat l'app, han vist la llista d'apps amb mida i dates, han cercat, ordenat i desinstal·lat alguna app confirmant-ho al diàleg del sistema. És l'ús real, tot i que un usuari en treu moltes d'un cop i un mòbil de prova en té poques.
```

**Resum dels suggeriments i com els has recollit** (252) ⚠ *si algún verificador dejó comentarios (en la consola o por correo), menciónalos.*

```
Pocs comentaris escrits dels verificadors; els he recollit des de la consola de Play i GitHub. Les millores han sortit de les meves proves en un mòbil real amb lletra gran i del banc de proves: el botó enrere a Android 16 i el comptador que es tallava.
```

## Informació sobre l'aplicació

**A quin públic objectiu va dirigida?** (186)

```
Qualsevol persona que vulgui fer neteja al mòbil i desinstal·lar moltes apps d'un cop, sense buscar-les una per una a la configuració. Sense anuncis, sense compte i sense recollir dades.
```

**Com proporciona valor als usuaris?** (244)

```
Llista les apps instal·lades amb mida i dates, permet cercar, ordenar i marcar-ne moltes i les desinstal·la una darrere l'altra amb la confirmació del sistema. El permís de consultar paquets només serveix per fer la llista; no envia res enlloc.
```

**Instal·lacions esperades el primer any** — Propuesta: **0 - 10.000** (app nueva, sin promoción).

## Preparació per a la producció

**Quins canvis has fet en funció de la prova tancada?** (240)

```
He arreglat el botó enrere a Android 16 (tancava l'app), he afegit un gestor d'errors perquè cap error la tanqui, el comptador ja no es talla amb lletra gran, els avisos no nomenen instal·ladors d'altres i he afegit 168 proves automàtiques.
```

**Com has decidit que està preparada per a producció?** (281) ⚠ *comprueba en Qualitat › Android Vitals que no hay fallos; si los hay, quita «sense tancaments a la consola». La declaración de `QUERY_ALL_PACKAGES` tiene que estar aceptada.*

```
Les 168 proves automàtiques passen totes, l'he provada en un mòbil real amb Android 16 sense errors, els verificadors l'han fet servir 14 dies sense tancaments a la consola i la fitxa, la privadesa i la seguretat de les dades i la declaració de consulta de paquets estan completes.
```
