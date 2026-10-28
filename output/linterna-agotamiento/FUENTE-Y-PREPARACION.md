# Linterna — agotamiento

Elegido por el usuario el 2026-10-28: Dying Light Bulb — Mike Koenig.

- Fuente: https://soundbible.com/1295-Dying-Light-Bulb.html
- Licencia indicada: Attribution 3.0 (CC BY 3.0), https://creativecommons.org/licenses/by/3.0/
- Crédito: «Dying Light Bulb — Mike Koenig, SoundBible, CC BY 3.0; conversión mono, remuestreo y fade inicial».
- MP3 fuente descargado y conservado: `dying_light_bulb_fuente.mp3`.
- Derivado: `linterna_agotamiento_dying_light_bulb.wav`, 2 s, mono PCM24/48 kHz, grabación completa con fade inicial de 8 ms. Sin pitch ni cambio de velocidad.
- Sustituirá el sonido de `linternaSeAgota` cuando la carga llega a cero. El aviso `bateriaBaja` también se sustituye, mediante el recorte de ondas 02+03 descrito abajo; el clic `linternaVacia` se conserva.
- Integrado en código y catálogo el 2026-10-28. Pendiente calibración auditiva y prueba en Unity/dispositivo.

Actualización del pedido: `linterna_bateria_baja_ondas_02_03.wav`, tramo 0,380–0,640 s del derivado completo, 0,260 s mono PCM24/48 kHz, fades de 5 ms, sin cambiar pitch/velocidad. Segunda y tercera onda juntas. Reglas y sincronización en `docs/LINTERNA-AVISOS-BATERIA-SINCRONIZADOS-HANDOFF.md`.
