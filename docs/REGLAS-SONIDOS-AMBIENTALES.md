# Reglas de sonidos ambientales

## Condición de entrega — audios listos (2026-10-13)

**Todo audio seleccionado en este documento debe existir descargado y preparado dentro del proyecto antes de asignarlo al agente implementador.** No dejar una URL o un archivo en Downloads como sustituto del asset final. Las propuestas todavía sin elegir se identifican como ideas pendientes y no como audios listos.

Inventario completo con rutas absolutas, duración, formato y niveles: [INVENTARIO-AUDIOS-PREPARADOS.md](C:/Users/fnval/Desktop/git/mr-test-master/output/audio-entrega/INVENTARIO-AUDIOS-PREPARADOS.md). Contiene **48 WAV**, incluidos 3 archivos de referencia/demostración y 45 clips de uso previsto. Se verificaron existencia, decodificación completa y picos inferiores a 0 dBFS; resta audición y calibración en el dispositivo durante integración. Preparado no significa integrado: no se modificaron scripts, catálogo ni escenas.

| Material completado en esta revisión | Archivo preparado | Uso |
|---|---|---|
| Respiración de Arbmos | [arbmos_exhalacion.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/arbmos-presencia/arbmos_exhalacion.wav) · 5,157 s, mono PCM24/48 kHz | Emisión espacial individual; fades cortos; no loop continuo |
| Fondo nuevo | [dark_choir_singing_loop.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/ambiente/dark_choir_singing_loop.wav) · 153,552 s, estéreo PCM24/48 kHz | Loop con unión de 3 s; no agregar fade en cada vuelta ni reiniciar al morir/espectar |
| Banco inicial de pasos de Arbmos | `output/arbmos-pasos/arbmos_paso_01.wav` a `arbmos_paso_04.wav`; cuatro rutas individuales en el inventario | Copias del banco de Sorken para probar las reglas con configuración propia. Timbre compartido inicial, no nuevos pasos elegidos |
| Susurros elegidos del libro | [libro_susurros_superpuestos_fondo_x2_loop.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/audio-libro/libro_susurros_superpuestos_fondo_x2_loop.wav) · 32 s | Conservar voces superpuestas y capa tenue concatenada a ×2; usar este archivo, no versiones anteriores |

Las fuentes de respiración y coro también se conservaron dentro de sus carpetas de `output`; el agente no depende de Downloads. Procedencia y preparación junto a los archivos. Los previews HQ de Freesound ya descargados son las fuentes de los recortes: no se promete el WAV original que requiere login.

## Golpes de puerta

Clips:
- `Assets/Audio/Clips/Ambiente/golpes_puerta_01.wav`
- `Assets/Audio/Clips/Ambiente/golpes_puerta_02.wav`

Reglas:
- Evento aleatorio de tensión, entre 1 y 2 veces por noche.
- Solo puede ocurrir después de 5 a 10 segundos continuos sin eventos.
- Cada clip es una variante independiente: reproducir uno u otro, nunca ambos juntos.

## Manija de puerta

Clip: `Assets/Audio/Clips/Ambiente/manija_puerta_01.wav`

Reglas:
- Evento aleatorio de tensión con la misma condición de inactividad de 5 a 10 segundos.
- Es un único sonido; se reproduce de forma independiente a los golpes de puerta.

## Entrada de Sorken por puerta

Clip: `Assets/Audio/Clips/Sorken/entrada_puerta_crujido.wav`

Regla:
- Reproducir cuando Sorken comienza a entrar por una puerta.

## Pisadas de Sorken

Clips: `Assets/Audio/Clips/Sorken/paso_entrada_puerta_01.wav` a `paso_entrada_puerta_04.wav`.

Reglas:
- Cada archivo contiene una sola pisada.
- Sincronizar cada clip con el apoyo correspondiente de Sorken.
- No reproducir las cuatro pisadas como un único audio.

## Sorken — dolor/enfado al cubrirse y voz durante marcha cubierta

Selección confirmada: [Zombie/Monster roars, growls, grunts, devouring sounds (juicy) — umnachtung](https://freesound.org/people/umnachtung/sounds/533165/). Dos primeras vocalizaciones para alternar al cubrirse por primera vez; vocalizaciones del tramo **9–23 segundos** de la fuente para perseguir mientras está tapado. Esta referencia es temporal, no los números de vocalización 9 a 23. Fuente HQ descargada y conservada en `output/sorken-cobertura/zombie_monster_juicy_fuente_hq.mp3`.

| Función | Archivo preparado | Duración final | Tramo fuente |
|---|---|---:|---|
| Cubrirse — variante 01 | [sorken_cubrirse_dolor_enfado_01.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_cubrirse_dolor_enfado_01.wav) | 3,000 s | 0,85–4,82 s |
| Cubrirse — variante 02 | [sorken_cubrirse_dolor_enfado_02.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_cubrirse_dolor_enfado_02.wav) | 3,000 s | 6,65–9,00 s |
| Marcha cubierta — voz 01 | [sorken_marcha_cubierta_vocal_01.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_marcha_cubierta_vocal_01.wav) | 1,550 s | 11,20–12,75 s |
| Marcha cubierta — voz 02 | [sorken_marcha_cubierta_vocal_02.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_marcha_cubierta_vocal_02.wav) | 1,760 s | 13,30–15,06 s |
| Marcha cubierta — voz 03 | [sorken_marcha_cubierta_vocal_03.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_marcha_cubierta_vocal_03.wav) | 2,770 s | 15,83–18,60 s |
| Marcha cubierta — voz 04 | [sorken_marcha_cubierta_vocal_04.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_marcha_cubierta_vocal_04.wav) | 1,900 s | 18,78–20,68 s |
| Marcha cubierta — voz 05 | [sorken_marcha_cubierta_vocal_05.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/sorken_marcha_cubierta_vocal_05.wav) | 1,720 s | 21,28–23,00 s |

### Primera reacción a la luz

- Una sola variante al comenzar `CoverStarting`: dolor y enfado acompañan el gesto de taparse y retroceder. Alternar 01/02 entre intentos, sin reproducir ambas juntas ni repetir cada vez que recibe luz. Alcance inicial conforme al código actual: una reacción de cobertura por intento/aparición (`_coverStartPlayedThisAttempt`), no una sola para toda la partida.
- Duración actual: `sorkenCoverStartSeconds = 3` en las noches dev/prod; la revisión local usa 2,967 s. Los dos recortes están ajustados a 3 s sin cambio de pitch: atempo 1,3233 para 01 y 0,7833 para 02. Mantener correspondencia con la fase/animación efectiva, incluyendo posibles cambios de duración. No rellenar con silencio una reacción corta ni alargar gameplay para acomodar el clip.
- Si la fase termina antes por captura, retirada, cancelación o interrupción válida, cerrar esta voz suavemente. No dejar una reacción de cobertura sobre el scream de captura ni reiniciarla al pasar a `CoverWalking`. Pausa/tracking deben usar el mismo reloj efectivo que la fase visible.

### Persecución mientras está tapado

- Usar el banco de cinco vocalizaciones únicamente en `CoverWalking`, como capa de dolor/enfado que complementa las pisadas dinámicas. No usarlo durante `CoverStarting`, persecución descubierta, captura, retirada final o despawn.
- Elegir una vocalización por emisión, sin repetición inmediata y sin voces vocales simultáneas de la misma entidad. Propuesta inicial de mezcla: esperar 1–2 s después de terminar la reacción inicial; dejar 1,5–3 s entre el final de una emisión y la siguiente. Cadencia por calibrar, no valores ya implementados.
- Las voces no son loops ni pasos: no dispararlas por cada apoyo o frame iluminado, ni acelerarlas según distancia o velocidad. Las pisadas siguen la animación y traslación con su banco propio.
- Al abandonar marcha cubierta, cancelar futuras emisiones y finalizar suavemente la actual. Si retoma ese estado, volver a programar sin repetir contactos/vocalizaciones atrasadas. No cambiar objetivos o gameplay por la duración de la voz.
- Sorken es una entidad compartida: emisión espacial desde cabeza/torso, audible según posición y atenuación, deduplicada host/clientes y alineada con el estado visible. Una muerte local no debe silenciar la amenaza para jugadores vivos.
- No duplicar efectos antiguos equivalentes de cobertura; auditar referencias/watchers antes de integrar. Conservar grito de captura, crujido de entrada y pisadas ya seleccionados. Ninguna vocalización reinicia Dark Choir Singing.

Preparación: siete WAV mono PCM24/48 kHz, fades de entrada 8 ms y salida 80 ms (120 ms en voz 05). Reacciones con pico -9 dBFS por variante; marcha con ganancia común y máximo -12 dBFS, conservando diferencias relativas. La última voz se limita a 23,00 s por el tramo pedido, aunque su caída continúa más allá en la fuente. Cortes revisados técnicamente por silencio/energía; audición de cortes, ajuste temporal y mezcla en dispositivo pendiente. Integración pendiente; no se modificó código.

Licencia CC BY 4.0: crédito a umnachtung, enlaces a fuente/licencia y descripción de edición obligatorios. Detalles: [ATRIBUCION-Y-EDICION.md](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-cobertura/ATRIBUCION-Y-EDICION.md). Fuente preview HQ MP3; el WAV original requiere login.

Validación para el agente: primera exposición a luz, alternancia entre intentos, varias vueltas de marcha cubierta, pérdida/retoma de luz, cancelación temprana, captura, FPS bajo, tracking y host/clientes. Una reacción inicial por intento, voces sólo en marcha cubierta, cero duplicaciones y final suave al cambiar de estado.

## Sorken — scream de captura fatal (selección confirmada)

| Función | Audio elegido | Archivo preparado | Estado |
|---|---|---|---|
| Sorken te atrapa y causa game over | [Monster scream 1 — giddster](https://freesound.org/people/giddster/sounds/416049/) | [sorken_captura_fatal_scream.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-vocalizaciones/sorken_captura_fatal_scream.wav) · 2,747 s | Descargado y preparado; integración pendiente |

- Conservar la vocalización completa, sin loop: mono PCM24/48 kHz, fades de 8 ms/100 ms y pico de referencia -6 dBFS. Sin cambio de pitch o velocidad. Nivel final a calibrar en dispositivo.
- Disparar una vez cuando Sorken efectivamente atrapa al jugador y resuelve su muerte. Deduplicar por víctima/evento autoritativo. No usar al aparecer, entrar, perseguir, ser repelido ni en muertes por Arbmos/Veleth.
- La señal principal de captura/game over corresponde al jugador atrapado. Cualquier reproducción espacial para otros jugadores no debe dispararles audio de derrota ni generar duplicaciones host/clientes.
- Auditar los efectos actuales de captura y muerte por Sorken: sustituir la función equivalente, evitando apilar un scream antiguo sobre éste; conservar sonidos distintos de entrada y pisadas dinámicas. Mantener la retirada acordada de risa y música de derrota antiguas.
- Preservar el ataque del grito al pasar al coro breve de game over. No retrasar la muerte, pantalla ni interacción para esperar la duración del audio. Si el usuario reintenta o sale antes del final, finalizar la voz con fade breve.
- **Dark Choir Singing continúa en la misma posición temporal**, sin reiniciarse por captura, game over o espectador. Scream y stinger son capas independientes del fondo.
- Validar captura host/cliente, muerte repetida/notificaciones duplicadas, reintento temprano, espectador y muertes por otras entidades: una señal por captura, sin gritos ajenos ni voces residuales.
- Fuente HQ completa conservada en `output/sorken-vocalizaciones/monster_scream_1_fuente_hq.mp3`. Licencia CC0 indicada en ficha; procedencia y edición en [ATRIBUCION-Y-EDICION.md](C:/Users/fnval/Desktop/git/mr-test-master/output/sorken-vocalizaciones/ATRIBUCION-Y-EDICION.md). WAV original requiere login. Decodificación, duración/formato y nivel verificados; audición en juego pendiente.

## Veleth — arrastre dinámico y salida del libro (2026-10-13)

### Retirar el sonido actual de salida del libro — acuerdo explícito

**Antes de integrar los nuevos audios de Veleth emergiendo del libro, retirar la reproducción del sonido actual de esa salida.** El agente debe identificar el clip y todos sus disparadores/referencias activos, incluidos eventos de animación y watchers, para que el efecto antiguo no se superponga con las nuevas capas.

- Conservar el banco nuevo de apoyos/roces ya preparado y los susurros nuevos del libro. La capa orgánica Alien is crawling-003 está elegida y preparada abajo; una capa vocal adicional sigue sin seleccionar.
- Retirar únicamente el efecto antiguo de salida; no silenciar el canal completo, alterar la animación o eliminar otros sonidos por compartir el evento. No hace falta borrar el archivo original.
- Validar salida del libro en host/clientes y reinicios: cero reproducción del efecto antiguo, nuevas capas sin dobles disparos y susurros conservados.
- Estado: decisión documentada para el agente implementador; todavía no se modificó código ni catálogo.

Audio elegido: [Crawling on a Wooden Floor.wav — krnash](https://freesound.org/people/krnash/sounds/389794/), 16,882 s, mono; licencia CC0 indicada en la ficha revisada. Descargada la vista previa HQ MP3 y preparados diez segmentos en `output/veleth-arrastre`. Integración pendiente; el WAV original requiere login.

| Función | Audio / material | Regla | Estado |
|---|---|---|---|
| Locomoción / «pasos» de Veleth | `veleth_apoyo_01.wav` a `05` y `veleth_roce_01.wav` a `05`, en `output/veleth-arrastre` | Sonidos de apoyo y arrastre sincronizados con la animación y traslación efectiva, siguiendo el criterio dinámico de Sorken | Segmentos preparados; implementación pendiente |
| Veleth saliendo del libro — apoyos/roces | El mismo banco, con selección de apoyos/roces y perfil propios para la emergencia | Roce y esfuerzo físico de arrastre cuando apoya brazos y extrae el cuerpo del libro; sincronizado con la salida real | Segmentos preparados; perfil y mezcla de emergencia pendientes |

### Capa orgánica de salida — Alien is crawling-003 (elegido)

| Audio | Archivo preparado | Duración y regla | Estado |
|---|---|---|---|
| [Alien is crawling-003 — tosha73](https://freesound.org/people/tosha73/sounds/512951/) | [veleth_salida_libro_organica_8_3s.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-emergencia/veleth_salida_libro_organica_8_3s.wav) | **8,3 s**, desde el inicio efectivo de salida hasta el comienzo de persecución; una sola reproducción, complementando apoyos/roces | Descargado y preparado; integración pendiente |

- Duración actual observada: `Assets/Prefabs/Veleth.prefab`, `_emergenceDuration: 8.3`. `VelethDirector.StartHuntAfterEmergence` espera el valor devuelto por `BeginEmergence` antes de comenzar a perseguir. El valor por defecto del script (5 s) no sustituye el serializado del prefab; comprobar posibles overrides de la instancia durante integración.
- Edición: primeros 8,3 s del preview HQ de 17 s, sin aceleración ni cambio de pitch; mono PCM24/48 kHz, entrada suave de 25 ms y salida de 250 ms que termina a los 8,3 s. No es un loop. Fuente completa conservada en `output/veleth-emergencia/alien_is_crawling_003_fuente_hq.mp3`.
- **La duración manda desde la emergencia real, no desde el archivo.** Si se cambia ese tiempo, preparar una versión equivalente de la fuente y ajustar su salida al nuevo final. No alargar la salida ni retrasar la persecución para que termine un audio de 17 s. No continuar esta capa durante la persecución.
- Emitir espacialmente desde Veleth/libro, audible para los jugadores según atenuación. Un solo disparo por emergencia, sincronizado con la salida visible y sin duplicaciones host/clientes. Pausa y cancelación deben respetar el reloj de la emergencia y finalizar suavemente la voz activa.
- Retirar primero el sonido antiguo de salida acordado arriba. Conservar los susurros nuevos y combinar esta capa con el banco preparado sin tapar sus contactos. Audicionar mezcla y final de transición en dispositivo.
- Licencia CC BY 4.0: crédito, fuente/licencia e indicación de recorte y fades en [ATRIBUCION-Y-EDICION.md](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-emergencia/ATRIBUCION-Y-EDICION.md). Derivado del preview HQ descargado; WAV original requiere login.

### Vocalizaciones — persecución y captura fatal (selección confirmada)

| Función | Audio elegido y archivo preparado | Regla | Estado |
|---|---|---|---|
| Scream de captura / game over por Veleth | [Demonic Woman Scream — nick121087](https://freesound.org/people/nick121087/sounds/333832/) → [veleth_captura_fatal_scream.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-vocalizaciones/veleth_captura_fatal_scream.wav), 4,458 s | Una vez cuando Veleth atrapa al jugador y resuelve su muerte; señal propia de Veleth, seguida del coro breve de derrota | Descargado y preparado; integración pendiente |
| Voz espectral durante persecución | [Ghost Monster Scream — NachtmahrTV](https://freesound.org/people/NachtmahrTV/sounds/556701/) → [veleth_persecucion_espectral_loop.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-vocalizaciones/veleth_persecucion_espectral_loop.wav), ciclo de 14,628 s | Capa adicional mientras persigue, complementando apoyos/roces dinámicos; no sustituye el arrastre ni la música general | Descargado y preparado; integración pendiente |

- Ambos son WAV mono PCM24/48 kHz para emisor espacial, derivados de previews HQ descargados. Fuentes completas conservadas en `output/veleth-vocalizaciones`. No se modificó pitch ni velocidad.
- Captura: se conserva el grito completo de 4,458 s, con fades de 8 ms/120 ms y pico de referencia -6 dBFS. No aplicar el recorte de 1–2 s ni la segunda voz grave que sólo se propusieron antes; no fueron pedidos al elegirlo.
- Disparar el scream desde la captura fatal autoritativa por Veleth, deduplicado por víctima/evento. No reproducirlo al invocar, por cercanía, al comenzar persecución ni en muertes causadas por otras entidades. La señal principal de game over corresponde al jugador atrapado; el tratamiento espacial para otros jugadores debe evitar dispararles audio de derrota o duplicar voces.
- Al capturar, atenuar la capa espectral para que el scream sea claro para la víctima. Luego reproducir el stinger de derrota con mezcla que preserve el grito; no apilar un jumpscare antiguo equivalente. No retrasar muerte, pantalla ni interacción para esperar los 4,458 s.
- Persecución: fuente de 15,378 s, unión final/inicio mediante crossfade lineal de 750 ms, ciclo preparado de 14,628 s. Empieza en el segundo 0,750 de la fuente. Pico de referencia -18 dBFS para capa secundaria; los niveles finales se calibran en juego. No agregar fade en cada vuelta.
- Iniciar la voz espectral al comenzar la persecución efectiva, después de la salida orgánica de 8,3 s. Propuesta de mezcla de estado: entrada de 250 ms y salida de 200 ms. No reiniciar por cada waypoint, cambio de objetivo o actualización de red; mantener una voz por entidad en cada dispositivo.
- El loop no sigue los apoyos ni se acelera con movimiento: esa sincronía pertenece al banco de arrastre. Emitir desde Veleth y acompañar su posición, con atenuación propia que no tape los contactos. Audicionar la unión y comprobar que la repetición no sature la mezcla.
- No reproducir esta capa durante la salida del libro ni mantenerla huérfana después de despawn/fin de noche. Pausa congela o atenúa coherentemente. Durante la pausa de agarre, salir suavemente; si vuelve a perseguir jugadores vivos, reactivar una sola voz. La muerte de una víctima no silencia globalmente la persecución si Veleth continúa con otros jugadores.
- Conservar los apoyos/roces y sus reglas dinámicas, Alien is crawling-003 exclusivamente durante emergencia y los susurros nuevos del libro. Auditar `velethPersecucion` y efectos actuales de captura para integrar las nuevas funciones sin duplicar pistas equivalentes; preservar capas distintas de tensión ya acordadas.
- **Dark Choir Singing conserva su reproducción y posición temporal** al capturar, perder y espectar. Scream, voz espectral y stinger usan voces separadas; ninguna reinicia el fondo.
- Validar persecución larga (varias vueltas), captura a mitad del loop, pausa de agarre, cambio de víctima, host/clientes, reintento y salida temprana de game over. Cero scream en muertes ajenas, cero voces duplicadas y transición clara hacia el coro de derrota.
- Licencia CC0 indicada en ambas fichas. Procedencia y edición: [ATRIBUCION-Y-EDICION.md](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-vocalizaciones/ATRIBUCION-Y-EDICION.md). Verificados duración/formato, decodificación y ausencia de saturación digital; escucha y calibración en dispositivo pendientes.

### Rutas de los segmentos preparados

| Clip | Duración | Tramo en fuente | Ruta |
|---|---:|---|---|
| Apoyo 01 | 0,40 s | 0,81–1,21 s | [veleth_apoyo_01.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_apoyo_01.wav) |
| Apoyo 02 | 0,43 s | 3,78–4,21 s | [veleth_apoyo_02.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_apoyo_02.wav) |
| Apoyo 03 | 0,42 s | 7,02–7,44 s | [veleth_apoyo_03.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_apoyo_03.wav) |
| Apoyo 04 | 0,34 s | 11,16–11,50 s | [veleth_apoyo_04.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_apoyo_04.wav) |
| Apoyo 05 | 0,58 s | 14,50–15,08 s | [veleth_apoyo_05.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_apoyo_05.wav) |
| Roce 01 | 1,40 s | 1,25–2,65 s | [veleth_roce_01.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_roce_01.wav) |
| Roce 02 | 1,30 s | 4,39–5,69 s | [veleth_roce_02.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_roce_02.wav) |
| Roce 03 | 1,11 s | 7,85–8,96 s | [veleth_roce_03.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_roce_03.wav) |
| Roce 04 | 1,59 s | 11,85–13,44 s | [veleth_roce_04.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_roce_04.wav) |
| Roce 05 | 1,35 s | 15,25–16,60 s | [veleth_roce_05.wav](C:/Users/fnval/Desktop/git/mr-test-master/output/veleth-arrastre/veleth_roce_05.wav) |

Formato: WAV mono PCM24, 48 kHz. Ajuste uniforme de +18,4 dB, pasaaltos 60 Hz, fades de 8 ms de entrada y 35 ms de salida, sin cambiar pitch. No se normalizó cada fragmento por separado para preservar diferencias relativas. Revisión de cortes basada en forma de onda y detección de silencios; audicionar y calibrar contra animación/dispositivo antes de dar por finalizado el perfil. Apoyos/roces son familias de uso propuestas, no asignaciones definitivas a mano/pie.

No reproducir apoyo y roce de igual número como secuencia obligatoria: seleccionar según el contacto y tramo de movimiento. Los roces incluyen textura y pequeños contactos internos, no son loops sostenidos preparados; ajustar longitud/mezcla si se requiere un arrastre más largo. Fuente y edición registradas en `output/veleth-arrastre/ATRIBUCION-Y-EDICION.md`.

Crawling on a Wooden Floor aporta apoyos/roces y Alien is crawling-003 aporta cuerpo orgánico durante la salida. Una vocalización adicional de esfuerzo y una señal de tensión independiente siguen sin seleccionar; no son archivos pendientes de esta entrega.

### Instrucciones para el agente — Veleth

- Usar y audicionar el banco segmentado de la tabla; ajustar los cortes si lo requiere la animación, conservando cuerpo y caída. No reproducir la grabación completa en bucle como sustituto de locomoción dinámica.
- Compartir infraestructura de eventos de contacto con Sorken cuando convenga, manteniendo perfiles propios de Veleth: el patrón de manos, brazos y arrastre no equivale a los pasos bípedos de Sorken.
- Disparar cada segmento en el apoyo o comienzo del arrastre visible. Para un roce prolongado, ajustar duración/intensidad al tramo animado y desplazamiento correspondiente; no hacer que el clip mande velocidad de gameplay.
- La cadencia sigue fase de animación y velocidad efectiva de traslación. No usar distancia a cámara como reloj ni contar correcciones de red/anchor como movimiento. Evitar derivar ritmo directamente de velocidad instantánea oscilante por frame.
- En salida del libro, usar perfil independiente de emergencia: los apoyos/roces pueden sonar aunque el root permanezca fijo, porque brazos y torso se desplazan durante la animación. No aplicar un filtro de velocidad del root que silencie toda la salida.
- Deduplicar contactos durante cross-fades, reinicios y vueltas de clip; evitar repetición inmediata entre segmentos compatibles. No reproducir atrasos en ráfaga después de FPS bajo/pausa.
- Sin locomoción ficticia durante quietud o ruta bloqueada; permitir sólo contactos explícitos de cambios de apoyo visibles. Finalizar roces activos suavemente al parar/capturar/despawnear.
- Audio espacial desde manos/base/cuerpo, próximo al libro mientras emerge y siguiendo a Veleth al perseguir. Usar `AudioManager` existente y configuración de mezcla propia.
- Veleth es compartida: host/clientes oyen su locomoción según posición y atenuación. No copiar el filtro de dueño individual de Arbmos. Alinear audio con animación visible en cada dispositivo, sin dobles disparos por replicación.
- Al agregar esta capa, auditar sonidos actuales de invocación y `velethPersecucion` para evitar un loop antiguo de arrastre duplicado. No borrar música de tensión ni señales distintas sólo por compartir estado de persecución.
- La salida debe poder combinarse luego con esfuerzos vocales y tensión sin ocultar contactos físicos. No usar el stinger de game over como aviso de emergencia ni modificar la continuidad del fondo Dark Choir Singing.
- Probar emergencia, persecución a distintas velocidades, pausa, ruta bloqueada, captura, reinicio, host/clientes y recalibración. Un sonido por contacto definido, roces proporcionales al movimiento y cero reproducción residual.
- Antes de implementar, revisar `Assets/Entities/Veleth/VelethEntity.cs`, `VelethGfx.cs`, `VelethDirector.cs`, sus animaciones y el watcher de audio. Las decisiones de mezcla deben respetar las fases existentes, no inventar una segunda secuencia de salida.

Referencia técnica de reglas compartidas: `docs/AUDIO-DINAMICO-SORKEN-AMBIENTE-HANDOFF.md`.

## Arbmos — selección de audio (2026-10-13)

### Retirar sonidos antiguos — acuerdo explícito

| Sonido actual | Acción para el agente | Qué se conserva |
|---|---|---|
| Aparición de Arbmos | Quitar la reproducción del sonido antiguo de aparición y sus disparadores/referencias activos; no superponerlo a los nuevos audios | Exhalación elegida, resonancia de presencia, enfado, pasos y agarres nuevos |
| Inicio del consumo del libro | Quitar el aviso/efecto antiguo que suena cuando empieza a consumirse; identificar su clip y disparador concreto antes de editar | **Los susurros nuevos del libro permanecen. No son el sonido que se pide retirar** |

Instrucciones:

- Auditar catálogo, watchers y vista/director del libro para identificar todos los disparos del efecto antiguo, incluidos posibles clips compuestos. No desactivar el canal completo ni toda la fase de consumo.
- Como referencia histórica, el watcher usaba `arbmosAparece` para una aparición y también al pasar a letal; verificar ambos usos y retirar la reproducción del asset antiguo sin silenciar las señales nuevas de la transición.
- Mantener los susurros preparados en `output/audio-libro/libro_susurros_superpuestos_fondo_x2_loop.wav` y la regla de activación que se defina para ellos. No borrar, sustituir ni silenciarlos como parte de esta retirada.
- No alterar sonidos de libro salvado/perdido, ni reglas de cordura/consumo, salvo instrucción aparte. Retirar reproducción/referencias activas; no borrar archivos originales sin necesidad.
- Validación: aparición normal/letal de Arbmos e inicio de consumo del libro sin efectos antiguos; susurros nuevos audibles cuando corresponda, sin dobles disparos.

Pendiente de implementación: esta actualización sólo documenta la decisión.

Todos los audios seleccionados están preparados dentro del proyecto, según el inventario enlazado arriba. Las nuevas piezas están en `output`; su importación a Assets e integración siguen pendientes. No se modificó el catálogo o el código.

| Función | Audio elegido / archivo original | Regla de diseño | Estado |
|---|---|---|---|
| Exhalación / firma de presencia | `output/arbmos-presencia/arbmos_exhalacion.wav` (5,157 s) | Debe parecer emitida por Arbmos y permitir ubicarlo. Propuesta: sonido espacial desde cabeza/torso, con silencios entre emisiones y poca reverberación; no confundirlo con daño | Preparado; frecuencia y mezcla en juego por calibrar |
| Enfado por exposición a la luz / inicio de persecución | `output/arbmos-enfado/arbmos_enfado_01.wav` (1,42 s), `arbmos_enfado_02.wav` (1,445 s), `arbmos_enfado_03.wav` (1,50 s) — Monster Growls, MeijstroAudio | Elegir una sola variante cuando la exposición válida a la luz compromete el ataque y comienza a perseguir. No disparar por cada frame iluminado; evitar repetición inmediata | Tres ondas recortadas y preparadas; integración pendiente |
| Pisadas durante persecución | `output/arbmos-pasos/arbmos_paso_01.wav` a `arbmos_paso_04.wav`; banco inicial preparado con copias de Sorken y configuración propia | Una pisada por apoyo real durante persecución normal y letal, sincronizada con animación y velocidad efectiva; sólo audible para el dueño de Arbmos | Banco inicial preparado; mezcla y contactos por validar; implementación pendiente |
| Agarre no letal / mini scream leve | `output/arbmos-vocalizaciones/arbmos_agarre_no_letal_gasp.wav` (0,471 s), de Male Gasp — nicklas3799 | Una vez cuando te alcanza y el resultado es no letal, conservando cordura; sonido contenido, sin comunicar game over | Elegido y preparado; integración pendiente |
| Agarre letal / game over por cordura agotada | `output/arbmos-vocalizaciones/arbmos_agarre_letal_primera_onda.wav` (1,85 s), de Male Inhale Scream 5 — jorickhoofd | Una vez en captura/muerte por Arbmos letal con cordura agotada. Sólo primer grupo de onda, con reducción moderada de graves para hacerlo menos animal | Elegido y preparado; integración pendiente |
| Nueva música ambiental general — reemplaza la actual | `output/ambiente/dark_choir_singing_loop.wav` (Dark Choir Singing, 153,552 s) | Reemplazar el fondo anterior `Assets/Audio/Clips/Musica/ambiente_noche.mp3`. Coro tenue durante la noche; continúa como fondo al permanecer en game over o espectar. Separado de respiración, daño y señal breve de derrota | Loop preparado; integración pendiente |
| Resonancia / tensión: entrada | `output/arbmos-resonancia/arbmos_resonancia_entrada.wav` (0,85 s) | Iniciar una vez al activar la capa; pasar al sostenido con crossfade aproximado de 150 ms | Preparado; integración pendiente |
| Resonancia / tensión: sostenido | `output/arbmos-resonancia/arbmos_resonancia_sostenida_loop.wav` (1,01 s) | Repetir mientras Arbmos está presente, según pedido del usuario; complementar respiración sin taparla. Unión interna preparada con mezcla de 240 ms; no aplicar fade en cada vuelta | Preparado; integración pendiente |
| Resonancia / tensión: salida | `output/arbmos-resonancia/arbmos_resonancia_salida_natural.wav` (0,78 s) | Al apagar la luz, salir del sostenido con crossfade aproximado de 150 ms y reproducir una vez la caída natural; no esperar al final del loop ni cortar abruptamente | Preparado; integración pendiente |
| Referencia de las primeras dos ondas | `output/arbmos-resonancia/arbmos_primeras_dos_ondas.wav` (8,25 s) | Conserva los dos primeros grupos de resonancia y sus caídas; referencia de edición, no reproducción simultánea con las piezas | Preparado |
| Demostración de la transición | `output/arbmos-resonancia/arbmos_resonancia_demostracion.wav` (9,41 s) | Entrada → ocho ciclos sostenidos → salida natural. Sólo para revisión; no usar como loop de gameplay | Preparado |
| Indicador específico de daño de cordura | Resonancia elegida como material de tensión; disparador/diferenciación pendiente | El último pedido fija sostenido durante presencia y salida al apagar luz. No asumir que ese sostenido equivale a daño continuo: definir acento o modulación para pérdida real de cordura si se necesita distinguirla | Definición de gameplay pendiente |

Para la futura integración, respetar que Arbmos es individual: su exhalación, pasos, enfado y señal de daño sólo los oye el jugador afectado. Dark Choir Singing es el fondo general de la experiencia, no una capa exclusiva de Arbmos. La reproducción del fondo es local en cada dispositivo y respeta volumen/opciones y pantalla de cada jugador. Son reglas pendientes de integración, no cambios ya realizados.

No reutilizar automáticamente la exhalación como señal de consumo. La resonancia fue elegida después de buscar tensión para acompañarla; distinguir presencia y daño real en la futura integración. Antes de hacer loop de otros clips, revisar su unión y nivel; no asumir que los originales ya están preparados para repetición.

### Resonancia — instrucciones para el agente implementador

- Fuente elegida: [Cello bow + small beer glass — giddster](https://freesound.org/people/giddster/sounds/662230/). Se usó el MP3 de vista previa HQ disponible públicamente; el WAV original requiere login. Los derivados son WAV estéreo PCM16 a 48 kHz, pero convertir el preview no recupera la calidad del original.
- Copiar las tres piezas funcionales a Assets cuando se autorice implementar; conservar las referencias y demostración para revisión. Los tiempos de origen y edición están en `output/arbmos-resonancia/LEEME.txt`.
- Reproducción: **entrada → sostenido en loop → salida al apagar la luz**. La salida debe poder comenzar en cualquier fase del loop mediante crossfade; necesita dos voces o mecanismo equivalente, no cambiar el clip y cortar la voz activa.
- No reiniciar entrada/sostenido por cada actualización de red. Reproducir la salida una sola vez por transición a luz apagada. Evitar apilar colas al alternar rápidamente; decidir retoma si la luz vuelve antes de completar salida.
- Apagar luz termina esta capa sonora, no cambia por sí mismo la regla de desaparición de Arbmos ni el daño. No usar duración del audio como temporizador de gameplay.
- Despawn, fin de noche y cancelación deben limpiar el sostenido con salida suave cuando corresponda; pausa congela o atenúa coherentemente. Si se reactiva, no duplicar voces anteriores.
- Sólo escucha esta capa el dueño de la aparición individual. Calibrar volumen con respiración y coro, preservando legibilidad.
- Validar encendido/apagado a mitad de loop, alternancia rápida, desaparición durante salida y host/cliente. Sin clicks, colas duplicadas ni sostenidos huérfanos. Los 150 ms son un punto inicial a ajustar escuchando en dispositivo.

### Pisadas de Arbmos — mismas reglas que Sorken

Pedido del usuario: cuando Arbmos persigue, debe escucharse el sonido de sus pasos con las mismas reglas de Sorken. Esto aplica a persecución normal y letal; no significa que comparta necesariamente clips, volumen o cadencia numérica.

- Un apoyo real = una variante individual. No concatenar las cuatro pisadas ni reproducirlas como loop sonoro continuo.
- Usar perfiles de contacto propios de la animación de Arbmos. Compartir detector/emisor con Sorken cuando convenga, pero no copiar sus tiempos ni cantidad de apoyos por ciclo.
- El ritmo sigue fase de animación y velocidad efectiva, incluidas variaciones por pie plantado. Cercanía sólo modifica atenuación/mezcla, no genera más pasos ni acelera su cadencia.
- Elegir variantes evitando repetición inmediata. Usar el banco inicial ya preparado en `output/arbmos-pasos`; contiene copias de las cuatro pisadas de Sorken, sin modificar sus originales. Configuración propia para calibrar timbre y nivel; un futuro banco de timbre diferente requiere selección y preparación explícitas.
- Emitir desde pie/base de Arbmos y sólo para el jugador dueño de la aparición. Filtrar copias no renderizadas en host; no convertir la alucinación individual en un sonido compartido.
- Sin pisadas periódicas durante aparición quieta, acecho inmóvil, pausa de golpe o bloqueo sin apoyos. Si hay un apoyo visible en el sitio, tratarlo como contacto explícito, no inferirlo por jitter de posición.
- Evitar dobles disparos en cross-fades, reinicios y vueltas de clip. No emitir contactos atrasados en ráfaga al caer FPS o recuperar tracking.
- Despawn, muerte y fin de noche cancelan nuevos pasos y limpian el seguimiento de contactos. Apagar la luz por sí solo no silencia pasos si Arbmos continúa una persecución comprometida: manda su locomoción real, no el estado de la capa de resonancia.
- Mezclar pasos con respiración y resonancia sin tapar señales de daño; reutilizar `AudioManager` y pooling existentes.
- Validación: persecución normal/letal a distintas velocidades, aparición quieta, transición a ataque, obstáculos, FPS bajo y host/cliente. Un sonido por contacto definido y cero pasos audibles de Arbmos ajenos.

Referencia de arquitectura compartida: `docs/AUDIO-DINAMICO-SORKEN-AMBIENTE-HANDOFF.md`, sección de pasos dinámicos y extensión de Arbmos.

### Agarre no letal y captura letal — instrucciones para el agente

- Separar pistas y eventos: jadeo breve para golpe no letal; primera onda del grito para captura letal. Ambos los oye únicamente el jugador afectado.
- No disparar el grito letal simplemente al llegar la cordura a cero o al comenzar el acecho: corresponde a cuando Arbmos efectivamente te alcanza y resuelve la muerte. Si el código permite muerte por timeout sin alcance, definir cómo acompañar esa resolución sin simular un contacto inexistente.
- Resolver sonido desde el resultado autoritativo del golpe/captura, deduplicado por evento. No inferirlo por distancia o por el estado de animación genérico. Evitar que suenen ambas pistas en el mismo golpe.
- Si un golpe normal deja cordura en cero y abre persecución letal posterior, no adelantar el grito de muerte: diferenciar pérdida de cordura de captura final. La regla de audio para ese golpe de transición queda por definir; el jadeo acordado describe el caso donde todavía queda cordura.
- La edición no letal conserva el jadeo completo con fades mínimos y pico de referencia de -12 dBFS; calibrar la mezcla en juego para que sea leve pero legible.
- La edición letal usa fuente 0–1,85 s, corte antes de la segunda onda, pasaaltos a 90 Hz y reducción de 4 dB alrededor de 250 Hz; fade final de 170 ms y pico de referencia de -6 dBFS. No se cambió pitch ni se añadió rugido. Valores de edición iniciales, sujetos a escucha del usuario/dispositivo.
- Ambas piezas son WAV estéreo PCM24, 48 kHz, derivados de vistas previas HQ MP3; los WAV originales requieren login. No confundir conversión a WAV con recuperar calidad sin pérdida.
- Pausar/atenuar las capas de respiración y tensión según convenga para que el agarre sea claro, sin superponer un segundo jumpscare viejo. No modificar daño, cordura o reglas de muerte por la duración del audio.
- El enfado previo a perseguir usa las tres variantes de Monster Growls de MeijstroAudio registradas en la tabla; mantenerlo separado de estos sonidos de agarre.
- Probar golpe con cordura restante, golpe que llega a cero sin muerte inmediata, captura letal, reinicio y host/cliente: cero dobles disparos y ningún aviso de game over en el golpe no letal.

### Enfado — instrucciones para el agente

- Fuente elegida: [Monster Growls — MeijstroAudio](https://freesound.org/people/MeijstroAudio/sounds/410523/). Tres vocalizaciones independientes recortadas de la vista previa HQ MP3, conservando su caída y sin cambiar pitch ni añadir procesamiento de timbre.
- Recortes del original: 01 = 0–1,42 s; 02 = 2,975–4,42 s; 03 = 5,71–7,21 s. WAV estéreo PCM24 a 48 kHz; fades mínimos y pico de referencia -8 dBFS por variante, a calibrar en la mezcla real.
- Emitir una variante desde Arbmos, sólo para el dueño de la aparición, una vez por transición a persecución normal causada por exposición suficiente. La luz que todavía no alcanzó el umbral no inicia este aviso. No reproducir las tres juntas, concatenadas ni como loop.
- Sin repetición inmediata si existen otras variantes válidas. Deduplicar por entidad/intento de ataque; actualizaciones de red o un cross-fade no vuelven a dispararlo.
- Es un aviso de enfado, no un game over ni un reemplazo de las pisadas. No retrasar el movimiento o modificar daño por la duración del clip. Uso al iniciar persecución letal por cordura cero queda por definir: ese inicio no necesariamente fue causado por apuntar con luz.
- Atribución y detalles junto a los clips en `output/arbmos-enfado/ATRIBUCION-Y-EDICION.md`. Los originales WAV requieren login; estos derivados usan preview HQ.

### Game over — retirar risa y música actuales (acordado 2026-10-13)

Pedido explícito del usuario: **sacar la risa y la música que actualmente suenan en game over**. Pendiente para el agente implementador; esta actualización sólo documenta, no modifica código ni catálogo.

| Elemento | Acción requerida | Estado |
|---|---|---|
| Risa actual de game over | Identificar su fuente y todos los disparadores; retirar su reproducción en game over | Acordado; implementación pendiente |
| Música actual de game over | Retirar la reproducción de `AudioCatalog.derrotaMuerte` al morir; revisar otros disparadores de derrota | Acordado; implementación pendiente |
| Música ambiental anterior | Retirar del fondo activo `Assets/Audio/Clips/Musica/ambiente_noche.mp3` y reemplazar su referencia por Dark Choir Singing; evitar cualquier fallback que reactive el tema anterior | Acordado; implementación pendiente |
| Nueva señal de derrota: Dark Choir Stinger — zombieallen | `output/gameover-coro/gameover_coro_breve.wav` (6 s), derivado de [Dark Choir Stinger](https://freesound.org/people/zombieallen/sounds/725222/) | Elegido y preparado; integración pendiente |
| Ambiente después de perder | Mantener o recuperar **Dark Choir Singing**, el nuevo fondo elegido; continuar si permanece en game over o entra a espectador | Acordado; implementación pendiente |

**Continuidad acordada: la música de fondo NO se reinicia por perder, entrar a espectador ni volver a game over.** Dark Choir Singing mantiene la misma reproducción y posición temporal; Dark Choir Stinger entra como una capa independiente por encima. Sólo atenuar/restaurar suavemente el volumen del fondo. «Recuperar el ambiente» significa recuperar volumen, no volver a reproducir desde cero. Si el fondo estaba pausado, continuar desde su posición conservada; no reiniciarlo por estos cambios de estado.

- Referencia observada: `Assets/Gameplay/LocalDeath.cs` llama a `AudioManager.Musica(c => c.derrotaMuerte, fade: 0.4f)`; el slot está en `Assets/Audio/AudioCatalog.cs`. El agente debe revisar el estado actual antes de editar.
- La fuente concreta de la risa no fue identificada en esta revisión puntual. Auditar clips de muerte/jumpscare, pistas compuestas, watchers, fuentes de escena y catálogo: si está dentro de un clip, quitar ese tramo o sustituir la pista, no silenciar todos los efectos de muerte.
- Conservar el grito letal nuevo de Arbmos seleccionado en la tabla; retirar la risa y música antiguas no implica eliminar ese sonido ni el jadeo no letal.
- Decisión confirmada por el usuario: **al perder entra Dark Choir Stinger y después continúa Dark Choir Singing**, tanto al quedarse en game over como al espectar. Son dos archivos distintos: Stinger marca derrota una vez; Singing es el fondo general nuevo. Retirar risa, pista antigua `derrotaMuerte` y fondo anterior `ambiente_noche.mp3` de la reproducción activa. La mención anterior a «ambiente actual» no autoriza conservar el archivo antiguo.
- Reproducir coro una vez por muerte local, después del grito de captura sin taparlo; no repetir al alternar pantalla de game over/espectador. Atenuar brevemente el ambiente mientras entra el coro y devolverlo suavemente al terminar. Propuesta: recuperación en 1–2 s; no reiniciar la pista ambiental si ya está reproduciéndose.
- Mantener la voz/fuente de fondo y su reloj independientes del stinger y del estado de muerte local. No detenerla, recrearla, volver a llamar Play desde cero ni intercambiar su clip al iniciar/terminar el stinger o alternar game over/espectador. No resetear fase del loop. Esta regla no define cambios de música al salir al menú o iniciar una nueva partida.
- El coro es una señal corta, no un loop ni una nueva canción permanente de derrota. La versión preparada usa los primeros 6 s con salida de 1,5 s; escuchar en dispositivo para validar el recorte. Si el jugador permanece, sólo continúa el ambiente.
- Actualizar el catálogo/configuración para que `ambienteNoche` use Dark Choir Singing en lugar de la música anterior. Referencia observada: `AudioEventWatcher.ActualizarMusicaPartida` usa ese slot y lo detiene cuando `GameStarted` pasa a falso; coordinar el disparador con game over para que no corte el fondo nuevo solicitado. No cambiar globalmente el audio de jugadores vivos ni modificar música de menú/victoria sin instrucción.
- Al reintentar o salir al menú, hacer fade de la señal restante y pasar a la música correspondiente sin bloquear interacción. Retirar tensión/respiración/pasos de entidades que ya no deben sonar; conservar únicamente el ambiente apropiado.
- No modificar condiciones de muerte, pantalla de game over ni lógica de espectador por esta tarea de audio.
- Retirar referencias/disparadores de reproducción; no borrar los archivos originales sin necesidad.
- Validación: muerte por Sorken, Arbmos letal y Veleth, host/cliente, reintento y fin de noche. Cero risa y cero música antigua de game over; grito letal de Arbmos una sola vez cuando corresponda y golpe no letal sin audio de derrota.
- Comprobar además coro una vez al perder, **Dark Choir Singing** continuo al quedarse/espectar, cero reproducción del antiguo fondo en noche/game over/espectador, ausencia de reinicios al alternar vistas y transición limpia al menú/reintento. Si el espectador entra antes de terminar el coro, no iniciar otro; completar o atenuar el existente según mezcla.
- Referencia auditiva corregida: `output/gameover-coro/gameover_coro_y_ambiente_demostracion.wav` (18 s), mezclada con el loop preparado **Dark Choir Singing** de `output/ambiente/dark_choir_singing_loop.wav`. Se reemplazó la demostración anterior que usaba por error `ambiente_noche.mp3`. Es sólo una demostración: implementar las capas separadas para respetar configuración, tiempo de permanencia y cambios de escena.
- La demostración comienza en el inicio del fondo por ser un archivo de revisión; no representa un reinicio permitido al morir. En el juego, superponer el stinger en cualquier instante de la pista que ya venía sonando. Validar muerte en distintos puntos del fondo: la posición avanza de forma continua antes, durante y después, incluso al alternar vistas rápidamente.

### Fuentes y licencias revisadas

Revisión de páginas oficiales: 2026-10-13. Es una lectura de condiciones publicadas, no garantía de titularidad de todos los samples ni dictamen jurídico.

| Audio | Fuente | Licencia consultada | Uso previsto en juego vendido |
|---|---|---|---|
| Creepy demon heavy breathing | [Mixkit, categoría Breathe](https://mixkit.co/free-sound-effects/breathe/) | [Sound Effects Free License](https://mixkit.co/license/modal/sfxFree/) | Admite explícitamente videojuegos y proyectos comerciales, incorporado a un producto mayor |
| Dark Choir Singing | [Ficha Pixabay](https://pixabay.com/sound-effects/horror-dark-choir-singing-16805/) — atribuido allí a BrainClaim (Freesound) | [Pixabay Content License, términos completos](https://pixabay.com/service/terms/) y [resumen](https://pixabay.com/service/license-summary/) | Uso comercial permitido; integrarlo como parte del juego, sin distribuirlo como audio independiente |
| Cello bow + small beer glass y derivados | [Ficha Freesound — giddster](https://freesound.org/people/giddster/sounds/662230/) | CC0, indicada en la ficha consultada | Permite adaptación y uso comercial; conservar procedencia. Crédito voluntario: giddster / Freesound |
| Male Gasp y derivado no letal | [Ficha Freesound — nicklas3799](https://freesound.org/people/nicklas3799/sounds/467363/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Uso comercial con crédito, enlaces a fuente/licencia e indicación de edición |
| Male Inhale Scream 5 y primera onda editada | [Ficha Freesound — jorickhoofd](https://freesound.org/people/jorickhoofd/sounds/180300/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Uso comercial con crédito, enlaces a fuente/licencia e indicación de recorte/EQ |
| Monster Growls y tres variantes de enfado | [Ficha Freesound — MeijstroAudio](https://freesound.org/people/MeijstroAudio/sounds/410523/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Uso comercial con crédito, enlaces a fuente/licencia e indicación de recortes/fades/nivel |
| Dark Choir Stinger / coro breve de derrota | [Ficha Freesound — zombieallen](https://freesound.org/people/zombieallen/sounds/725222/) | CC0 indicada en ficha | Adaptación y uso comercial; preview HQ recortado a 6 s, fade y nivel ajustados. Crédito voluntario: zombieallen / Freesound |
| Alien is crawling-003 / salida orgánica de Veleth | [Ficha Freesound — tosha73](https://freesound.org/people/tosha73/sounds/512951/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Uso comercial con crédito, enlaces a fuente/licencia e indicación del recorte, downmix y fades |
| Zombie/Monster roars, growls, grunts (juicy) / cobertura y marcha de Sorken | [Ficha Freesound — umnachtung](https://freesound.org/people/umnachtung/sounds/533165/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Crédito, enlaces e indicación de siete recortes, ajustes temporales, downmix, fades y nivel |
| Monster scream 1 / captura fatal de Sorken | [Ficha Freesound — giddster](https://freesound.org/people/giddster/sounds/416049/) | CC0 indicada en ficha | Preview HQ completo preparado con downmix, fades y nivel; crédito voluntario recomendado |
| Demonic Woman Scream / captura fatal de Veleth | [Ficha Freesound — nick121087](https://freesound.org/people/nick121087/sounds/333832/) | CC0 indicada en ficha | Preview HQ completo preparado con downmix, fades y nivel; crédito voluntario recomendado |
| Ghost Monster Scream / voz de persecución de Veleth | [Ficha Freesound — NachtmahrTV](https://freesound.org/people/NachtmahrTV/sounds/556701/) | CC0 indicada en ficha | Preview HQ preparado como loop con downmix, unión y nivel; crédito voluntario recomendado |
| Crawling on a Wooden Floor / segmentos para Veleth | [Ficha Freesound — krnash](https://freesound.org/people/krnash/sounds/389794/) | CC0 indicada en ficha | Adaptación y uso comercial; diez recortes ya preparados y registrados. Crédito voluntario: krnash / Freesound |

Conclusión para el uso descrito: vender el juego con el coro integrado como parte de la experiencia parece compatible con Pixabay; la exhalación está expresamente permitida en videojuegos comerciales por Mixkit. La clasificación legal depende de cómo se distribuyen los audios, no de llamar al coro «música» o «efecto» dentro del juego.

Límites relevantes:

- Pixabay permite uso comercial y adaptación; prohíbe distribución standalone. Incorporarlo a una obra creativa mayor es distinto de vender el MP3 solo. Un cambio de volumen, recorte o filtro no basta por sí mismo para justificar vender el archivo como producto independiente.
- Pixabay no exige atribución. La ficha del coro muestra Pixabay Content License; no deducir otra licencia únicamente por el nombre `freesound_community`. No se verificó en esta revisión la cadena de derechos del material original de BrainClaim.
- Mixkit SFX permite distribución dentro del producto final; prohíbe redistribuir el efecto solo, como stock, herramienta/plantilla o con archivos fuente, y reclamarlo como propio o registrarlo en un servicio de gestión de derechos.
- No ofrecer estos archivos originales como descarga adicional, paquete de sonidos o banda sonora separada sin revisar permisos para esa distribución. Para repositorio público o entrega del proyecto Unity con fuentes, revisar especialmente la restricción de Mixkit; no equivale a distribuir una build del juego.
- Conservar registro de página de origen, fecha real de descarga y licencia aplicable a cada asset. La fecha de esta revisión no demuestra la fecha de descarga. Créditos voluntarios recomendables: «Dark Choir Singing — BrainClaim / freesound_community, vía Pixabay» y «Creepy demon heavy breathing — Mixkit».
- Las plataformas no sustituyen la comprobación de derechos de terceros cuando corresponda; las condiciones de uso comercial no garantizan por sí mismas la procedencia de cada sample.
