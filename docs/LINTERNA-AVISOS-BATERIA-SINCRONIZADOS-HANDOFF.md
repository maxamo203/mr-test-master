# Linterna — batería baja y agotamiento sincronizados con audio

Fecha: 2026-10-28. Diseño y referencia de la integración. El código y el catálogo se integraron en `main` el mismo día; falta la prueba perceptual en Unity y en dispositivo.

## Decisiones confirmadas

Fuente elegida: **Dying Light Bulb — Mike Koenig**.

1. Reemplazar el aviso actual de batería baja por **la segunda y tercera onda juntas**, como una única secuencia.
2. Reemplazar el sonido actual de agotamiento por **el audio completo**.
3. Cada onda debe acompañarse de un corte de luz: **se apaga en la subida y vuelve a encenderse en la bajada**.
4. En agotamiento, **la última onda apaga definitivamente la linterna**: no vuelve a encenderse al bajar esa onda ni al terminar el archivo.
5. El aviso de batería baja termina recuperando la luz, siempre que el jugador la mantenga encendida y todavía tenga carga.

## Archivos ya descargados y preparados

| Uso | Archivo | Duración / edición |
|---|---|---|
| Aviso de batería baja | [linterna_bateria_baja_ondas_02_03.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/linterna-agotamiento/linterna_bateria_baja_ondas_02_03.wav) | 0,260 s; tramo 0,380–0,640 s del archivo completo, conserva ondas 02 y 03 y la separación original; fades de 5 ms |
| Agotamiento | [linterna_agotamiento_dying_light_bulb.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/linterna-agotamiento/linterna_agotamiento_dying_light_bulb.wav) | 2,000 s completos; fade inicial de 8 ms, sin alterar velocidad/pitch |
| Fuente descargada | [dying_light_bulb_fuente.mp3](C:/Users/fnval/Desktop/git/mr-test-master/output/linterna-agotamiento/dying_light_bulb_fuente.mp3) | Fuente original descargada, no asignar además de sus derivados |
| Referencia visual de señal | [envolvente_referencia.png](C:/Users/fnval/Desktop/git/mr-test-master/output/linterna-agotamiento/envolvente_referencia.png) | Energía RMS en ventanas de 5 ms; referencia técnica, no una validación auditiva |

Derivados WAV mono PCM24 a 48 kHz. No concatenar repeticiones ni convertir el aviso en un bucle. El archivo completo tiene unos 0,8 s de actividad principal y después una cola prácticamente silenciosa: su duración de contenedor no equivale a 2 s de sonido sostenido.

Procedencia: https://soundbible.com/1295-Dying-Light-Bulb.html . Licencia indicada: CC BY 3.0, https://creativecommons.org/licenses/by/3.0/ . Crédito obligatorio: «Dying Light Bulb — Mike Koenig, SoundBible, CC BY 3.0; recortes, conversión mono, remuestreo y fades». Registro: [FUENTE-Y-PREPARACION.md](C:/Users/fnval/Desktop/git/mr-test-master/output/linterna-agotamiento/FUENTE-Y-PREPARACION.md).

## Estado actual comprobado

- `Assets/Flashlight.cs`: `bateriaBaja` suena una vez por bajada al alcanzar 20 % o menos con carga positiva, durante drenaje activo. Se rearma tras recargar por encima del 30 %.
- `linternaSeAgota` se dispara cuando la carga llega a cero y se apaga la linterna.
- `linternaVacia` corresponde a intentar encenderla sin batería: **no se reemplaza** en este encargo.
- El prefab `Assets/Prefabs/XR Origin.prefab` configura titileo aleatorio por debajo del 40 %. `UpdateFlicker` es visual: no cambia el modo lógico ni las reglas de defensa/red.
- El catálogo asigna `Linterna/bateria_baja.wav` y `Linterna/se_agota.wav` a los dos slots que se reemplazan. Verificar estado actual antes de implementar.

Conservar inicialmente umbrales 20 %/30 % y el drenaje vigente: no se solicitó cambiarlos.

## Marcadores iniciales de sincronización

Usar la envolvente de cada **grupo de sonido**, no cada oscilación de la onda PCM ni sus cruces por cero. Los tiempos siguientes se obtuvieron por energía/silencio; son puntos iniciales que el agente debe confirmar escuchando y viendo el resultado.

| Grupo del completo | Comienzo aproximado de subida | Bajada / recuperación aproximada | Aviso de batería baja | Agotamiento |
|---|---:|---:|---|---|
| 01 | 0,015 s | 0,125 s | No incluido | Corte y recuperación |
| 02 | 0,405 s | 0,460 s | Corte y recuperación | Corte y recuperación |
| 03 | 0,550 s | 0,610 s | Corte y recuperación | Corte y recuperación |
| Grupo final / cola 04 | 0,690 s | 0,760 s | No incluido | Corte definitivo; omitir recuperación |

La señal muestra un cuarto grupo menor después de las tres ondas principales. Conservarlo en el audio completo y tratarlo como último grupo para la salida definitiva; no apagar definitivamente en la tercera y luego mostrar un cuarto destello accidental.

En el recorte de batería baja restar **0,380 s** a los marcadores de los grupos 02 y 03. Referencia local: cortes en 0,025 y 0,170 s; recuperaciones en 0,080 y 0,230 s. No confundir los tiempos de fuente con los del recorte.

La curva visual debe permanecer apagada durante la subida y recuperar al descender. Confirmar el punto perceptual de bajada por escucha; los extremos de energía son referencias, no una obligación de esperar hasta silencio absoluto. No producir destellos extra dentro de un mismo grupo.

## Decisión técnica a cerrar: último destello frente a carga cero

Actualmente la batería llega a cero **antes** de iniciar el sonido de agotamiento. Si se conserva ese disparador, encender los primeros pulsos del audio exigiría mostrar luz después de agotada. No implementar esa contradicción silenciosamente ni agregar batería artificial.

**Propuesta de implementación:** anticipar la secuencia final durante el último remanente de carga, para que sus cortes/recuperaciones ocurran mientras aún existe energía y su último corte coincida con el agotamiento. Con esta edición, el tramo hasta el último corte es aproximadamente 0,690 s; el archivo completo sigue reproduciendo su caída/cola con la luz apagada. Usar drenaje efectivo y modo actual al calcular ese remanente, no el contenedor de 2 s.

Esto es una propuesta técnica, no un cambio de umbral ya aprobado. Antes de implementarla, resolver y registrar cómo responde al cambio tenue/intensa, pausa, recarga y drenaje modificado. Si el agente no puede mantener alineados el cero real y el último corte sin cambiar las reglas de energía, consultar ese punto concreto. No retrasar el agotamiento, congelar carga ni volver a habilitar defensa después de cero para completar audio.

Si la carga se pierde bruscamente y no hay margen para la secuencia final, la condición real de cero tiene prioridad: apagar y no emitir recuperaciones visuales inválidas. Documentar esa excepción y comprobarla.

## Arquitectura propuesta

1. Importar los dos derivados finales a `Assets/Audio/Clips/Linterna/`, con sus `.meta`. Asignar al catálogo central, sustituyendo sólo `bateriaBaja` y `linternaSeAgota`. Conservar originales históricos sin reproducción activa; no tocar `linternaVacia`, encendido, apagado o recogida de pilas.
2. Definir dos perfiles de presentación con clip, duración, marcadores de corte/recuperación y bandera de último corte definitivo. Referir los marcadores al archivo final asignado.
3. Usar `AudioManager` para reproducir y devolver/compartir una referencia al inicio real. No crear `AudioSource` por fuera, no tocar `AudioSettings` y no reproducir audio adicional al aplicar cada corte.
4. Audio y luz deben seguir el mismo reloj de reproducción. Preferir inicio programado con reloj DSP o mecanismo equivalente del sistema existente. No iniciar audio y una coroutine independiente suponiendo que van alineados.
5. Obtener el estado visual correcto a partir del tiempo actual; a FPS bajo no reproducir todos los cortes atrasados en ráfaga. No disparar una animación visual nueva por cada pico instantáneo.
6. Aplicar el corte a todas las salidas visuales de la linterna: shader, haz, iluminación real y oscuridad correspondiente. No dejar la luz real encendida mientras se corta sólo el cono dibujado.
7. Durante una secuencia sincronizada, suspender el titileo aleatorio para que no genere cortes fuera de las ondas ni tape sus recuperaciones. Al terminar batería baja, devolver control al titileo normal; tras agotamiento, mantener salida cero. No se solicita eliminar permanentemente el titileo aleatorio del 40 %.
8. Como criterio inicial, conservar el carácter visual de los parpadeos y no llamar `Toggle()` para cada pulso: evitar sonidos on/off, cambios de modo, liberación de hold o paquetes de red por onda. Una eventual afectación de defensa, libro o Arbmos sería un cambio de gameplay aparte, no implícito en esta presentación.
9. Una recuperación devuelve el modo/intensidad que corresponda en ese instante, no fuerza luz intensa ni restaura un estado viejo si el jugador apagó manualmente. Con carga cero, muerte, salida o cancelación, nunca recuperar luz automáticamente.
10. Host y clientes procesan el evento local de su batería. Si se representa el haz de otros jugadores, integrar la presentación con la replicación existente, evitando que host/clientes dupliquen sonidos. No sincronizar un mensaje por oscilación.

## Prioridad e interrupciones

- Agotamiento tiene prioridad sobre aviso bajo: cancelar/fundir el aviso anterior, reemplazar su secuencia visual y evitar dos voces simultáneas de estos avisos.
- Apagado manual durante aviso: cancelar los pulsos y no volver a prender por marcadores pendientes. Definir junto al reloj de audio una salida breve y limpia.
- Recarga suficiente antes de finalizar agotamiento: cancelar la presentación obsoleta. Una cola antigua no debe apagar una linterna recién recargada.
- Cambio tenue/intensa: recuperaciones respetan el modo pedido; si se anticipa agotamiento, reevaluar la predicción de carga sin reiniciar voces repetidamente.
- Pausa: audio y secuencia se pausan/reanudan juntos. No reanudar con un flash atrasado ni permitir que un reloj DSP siga consumiendo marcadores mientras la animación visual está congelada.
- Muerte, reinicio sin recargar escena, salida al menú y destrucción del objeto: cancelar voces, marcadores y estado visual, restaurando sólo lo que corresponda a ese estado. No interferir con presentación de muerte ni música.
- Aviso bajo conserva un disparo por bajada y rearme por encima del 30 %; no repetir con cada frame, con cada flicker ni al rozar el umbral.

## Validación y entrega del agente

- [ ] Audicionar ondas 02+03 juntas y archivo completo; ajustar marcadores perceptuales sin alterar la selección.
- [ ] Aviso bajo: exactamente dos cortes/recuperaciones ligados a las ondas, sin sonidos on/off adicionales.
- [ ] Agotamiento: pulsos previos sincronizados y último corte definitivo; sin destello al finalizar archivo.
- [ ] Confirmar apagado definitivo con carga real cero; documentar la solución elegida para anticipación y drenaje variable.
- [ ] Comprobar tenue/intensa, shader/luz real y ausencia de titileo aleatorio superpuesto.
- [ ] Verificar exposición/defensa y estados de entidades: no cambian por usar el efecto visual.
- [ ] Probar aviso único 20 %, rearme >30 %, recarga parcial, agotamiento inmediato, apagado manual, pausa, muerte, retry y salida.
- [ ] Probar FPS bajo y host/clientes sin ráfagas atrasadas ni duplicaciones.
- [ ] Actualizar atribuciones y documentación de referencia si se implementa. Registrar pruebas realmente ejecutadas y limitaciones.

## Estado de integración (2026-10-28)

- Clips importados a `Assets/Audio/Clips/Linterna/` y asignados a `bateriaBaja` y `linternaSeAgota` en el catálogo. Importación PCM precargada para leer posición de muestra.
- `AudioManager` usa una voz exclusiva y expone su posición de reproducción a `Flashlight`. Los cortes se calculan con esa posición; no se dispara `Toggle()` por onda.
- Agotamiento anticipado según drenaje efectivo y carga positiva, con apagado real al llegar a cero. Si el drenaje cambia a mitad de la secuencia se cancela y se recalcula desde la carga actual; si llega a cero de golpe se reproduce el audio completo con la luz ya apagada.
- El overlay y la `Light` real siguen los cortes. Los avisos se cancelan en recarga, apagado manual, muerte, retry, salida y desactivación. La pausa detiene la voz.
- Comprobado por inspección: duraciones, formato WAV, referencias del catálogo y `git diff --check`. **Pendiente:** abrir Unity 6000.4.4f1, validar compilación, escuchar los cortes, probar cambio tenue/intensa cerca del cero y ajustar los marcadores si la percepción lo exige.
