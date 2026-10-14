# Mortuorium — Implementación de sonidos ambientales y pasos dinámicos de Sorken

Fecha: 2026-10-12. Guía para otro agente. Estado: diseño pendiente de implementación.

## 1. Encargo y alcance

El usuario recopiló sonidos en `docs/REGLAS-SONIDOS-AMBIENTALES.md` y quiere incorporarlos, reemplazar los sonidos equivalentes existentes y crear pasos dinámicos de Sorken: **un sonido por apoyo, elegido entre las cuatro pisadas nuevas, siguiendo la velocidad y la animación real**.

En esta entrega sólo se documenta. No se modificaron scripts, escenas, prefabs, clips ni catálogo. Este documento no autoriza iniciar implementación por sí solo: el agente implementador debe recibir la instrucción del usuario de ejecutar el trabajo.

Actualización de assets (2026-10-13): respiración, coro nuevo, susurros y bancos segmentados están preparados en el proyecto. El banco inicial de Arbmos está en `output/arbmos-pasos/arbmos_paso_01.wav` a `04`; son copias de Sorken para la prueba con configuración propia. Los reemplazos de música y retirada de efectos acordados después de este handoff se rigen por el documento principal; conservar otras pistas no anula esas decisiones.

Fuente principal de reglas: `docs/REGLAS-SONIDOS-AMBIENTALES.md`. Contexto de gameplay: `docs/GAMEPLAY-REFINAMIENTO-PENDIENTES.md` (avance muy lento bajo luz, tolerancia de 2 s, espacios pequeños y reparto de eventos multijugador). Las propuestas aquí identificadas como tales necesitan validación; no tratar cifras antiguas del gameplay como implementación vigente sin revisar el código actual.

## 2. Assets nuevos y comportamiento requerido

| Grupo | Assets | Disparo y regla |
|---|---|---|
| Golpes de puerta | `Assets/Audio/Clips/Ambiente/golpes_puerta_01.wav`, `golpes_puerta_02.wav` | Evento aleatorio de tensión, 1–2 veces por noche; después de 5–10 s continuos sin eventos; elegir una variante, nunca las dos juntas |
| Manija | `Assets/Audio/Clips/Ambiente/manija_puerta_01.wav` | Evento aleatorio independiente; misma condición de 5–10 s de inactividad; no emparejarlo automáticamente con golpes |
| Entrada por puerta | `Assets/Audio/Clips/Sorken/entrada_puerta_crujido.wav` | Una vez cuando Sorken comienza efectivamente a entrar por puerta |
| Pisadas | `Assets/Audio/Clips/Sorken/paso_entrada_puerta_01.wav` a `paso_entrada_puerta_04.wav` | Cada archivo contiene una pisada; una variante por apoyo, sin concatenar las cuatro |

Los ocho WAV y sus `.meta` existen en las carpetas revisadas. La revisión del 2026-10-13 verificó existencia, duración/formato, decodificación completa y picos. La audición y calibración en dispositivo siguen pendientes. Inventario de todos los audios seleccionados: [INVENTARIO-AUDIOS-PREPARADOS.md](C:/Users/fnval/Desktop/git/mr-test-master/output/audio-entrega/INVENTARIO-AUDIOS-PREPARADOS.md). No dejar descargas ni recortes seleccionados como tarea pendiente de este handoff.

La regla 1–2 por noche corresponde al grupo de golpes, no a cada variante por separado. La fuente no define cuota de manija: dejarla configurable y documentar una propuesta moderada, sin inventar que comparte obligatoriamente esa cuota. Si no existen ventanas elegibles durante la noche, no forzar golpes para alcanzar el mínimo.

## 3. Diagnóstico actual

- `Assets/Audio/AudioEventWatcher.cs` dispara `sorkenPasos` sólo en `Chasing`, con intervalo entre 0,75 y 0,38 s según cercanía. El temporizador `_proximoPaso` no sigue apoyos, velocidad efectiva ni marcha cubierta. Reemplazar este mecanismo; no agregar el nuevo encima.
- El watcher dispara entrada por tipo al descubrir la entidad y `sorkenEntra` al cambiar a `Chasing`. Ninguno de esos disparos identifica por sí solo el inicio exacto de la animación de entrada por puerta. Evitar dos entradas audibles por un mismo ingreso o repetirlas al volver de cobertura a persecución.
- `Assets/Entities/Sorken/SorkenAnimator.cs` usa Playables, cross-fades y tiempo de clip. Tiene parámetros de 12 apoyos por loop de persecución y 5 de marcha cubierta; son datos configurados, no una verificación de los apoyos reales. Los clips pueden reiniciarse y el tiempo de locomoción se reduce módulo su duración.
- La velocidad de reproducción de persecución y la traslación se configuran separadamente. Sin resolver esa relación, cambiar sólo frecuencia de audio no hace coherentes pies, movimiento y sonido.
- `Assets/Audio/AudioCatalog.cs` y `Assets/Resources/AudioCatalog.asset` centralizan pistas y referencias. `Pista.Elegir` elige aleatoriamente y admite repeticiones consecutivas; no proporciona por sí sola una bolsa de variantes sin repetición.
- `Assets/Audio/AudioManager.cs` centraliza fuentes y reproducción espacial. Reutilizarlo; no crear un segundo motor ni fuentes por cada paso.
- `Assets/Editor/AudioCatalogSetup.cs` crea el catálogo si falta; si existe, lo selecciona sin actualizarlo. Ese menú solo no migra referencias.

## 4. Migración del catálogo y reemplazos

- [ ] Inventariar referencias actuales y dejar una tabla de antes/después en la entrega.
- [ ] Reemplazar las variantes de pasos activas por los cuatro WAV nuevos. Existen también `paso_01.ogg` a `paso_06.ogg`: no mantener su disparo simultáneo ni borrar sus archivos sin necesidad.
- [ ] Registrar grupos independientes para golpes, manija y crujido real de entrada. No cargar golpes/manija en slots de aparición de Sorken: son eventos ambientales y no implican que exista la entidad.
- [ ] Integrar crujido con el evento correcto y retirar/sustituir el sonido antiguo equivalente. Conservar entrada por ventana y otros sonidos no cubiertos por estos assets.
- [ ] Revisar `sorkenEntra`, entradas por tipo y estados de cobertura para eliminar duplicaciones; conservar una capa adicional sólo si tiene propósito sonoro diferente, documentado y mezclado.
- [ ] Mantener GUID y `.meta`, actualizar referencias de forma revisable y comprobar inclusión en build.
- [ ] Si se actualiza una herramienta de configuración, hacerla idempotente y preservar calibraciones ajenas. No regenerar todo el catálogo ni sobrescribir pistas de libro, Arbmos, Veleth, UI o linterna.

## 5. Pasos dinámicos: animación como reloj

### Regla central

**El apoyo del pie dispara la pisada.** La velocidad efectiva determina el ritmo de locomoción acordado con el gameplay; el audio sigue ese ritmo. La distancia al oyente cambia atenuación, no inventa pasos adicionales ni acelera su cadencia.

Propuesta de arquitectura:

1. Un perfil de apoyos por clip/estado describe momentos de contacto, pie izquierdo/derecho y, si corresponde, intensidad relativa.
2. Un detector consulta la fase efectiva de la animación y emite un evento lógico de apoyo por entidad, ciclo y contacto.
3. Un emisor de pasos elige una de las cuatro variantes y envía la reproducción posicional al `AudioManager` existente.

Usar marcadores normalizados en datos externos como opción inicial, calibrados viendo cada clip. Los eventos de animación también son una alternativa, pero comprobar su comportamiento real con este grafo Playables antes de adoptarlos. No asumir que dividir el loop en 12 o 5 partes iguales coincide con los apoyos.

### Estados a cubrir

| Estado/movimiento | Regla |
|---|---|
| Entrada por puerta | Crujido una vez al iniciar; pisadas sólo en contactos visibles definidos para ese clip |
| Persecución | Pisadas sincronizadas con marcha y velocidad efectiva |
| Marcha cubierta bajo luz | Mismo principio, ritmo lento coherente con el avance; no reutilizar temporizador de persecución |
| Gesto inicial de cobertura / quietud | No emitir pasos de locomoción; sólo un apoyo real específico si está marcado |
| Tolerancia al perder apuntado | Seguir lo que efectivamente haga cuerpo/animación; si se detiene, no producir pasos periódicos |
| Retirada | Pisadas si el clip muestra apoyos de desplazamiento; perfil propio |
| Entrada por ventana / aterrizaje | No reproducir crujido de puerta. Evaluar contactos e impacto por separado; no inventar un asset de impacto faltante |
| Captura, despawn, pausa y fin de noche | Cancelar nuevos pasos pendientes y limpiar estado; no emitir pisadas atrasadas |

### Velocidad, obstáculos y transiciones

- Calibrar relación de velocidad media, longitud de zancada y ritmo del clip. Revisar multiplicador global, marcha cubierta y modulación por pie plantado. No derivar ritmo directamente de velocidad instantánea por frame: oscila dentro de cada paso y puede incluir interpolación o correcciones de red.
- Preferir velocidad de locomoción efectiva compartida con la animación; si se estima por posición, filtrar ruido y excluir teleport/recalibración. Mantener un mínimo/máximo de playback legible y validar deslizamiento visual.
- Si ajustar playback altera desplazamiento modulado por animación, comprobar que no cambie sin intención la dificultad, ventana de repel ni distancia de captura. Documentar cualquier ajuste necesario al vínculo movimiento/animación.
- Ante bloqueo de ruta, detener locomoción de avance o representar una acción de apoyo en el sitio; no mantener marcha ficticia y simplemente silenciarla. Un cambio de apoyo visible puede sonar aun sin traslación; distinguirlo de pasos de avance.
- Cross-fades: elegir una fuente dominante de contactos o fusionar contactos equivalentes para evitar dos pisadas del mismo pie al mezclar clips.
- Usar identificación de ciclo/contacto para deduplicar. Tratar reinicios, wrap de tiempo y cambios de estado sin perder el primer apoyo ni duplicar el último.
- Con saltos grandes de fase o FPS bajo, descartar contactos obsoletos; nunca reproducir una ráfaga para ponerse al día. Puede haber dos apoyos reales cercanos: no imponer un cooldown que borre contactos válidos.

### Variantes y mezcla

- Elegir una variante por apoyo con bolsa aleatoria de cuatro o selección que evite repetición inmediata. No asociar automáticamente cada número de archivo a un pie o velocidad: son variantes, salvo que una escucha revele diferencias que justifiquen perfiles.
- Preservar carácter pesado. Propuesta: variación de pitch pequeña y volumen por intensidad de contacto; calibrar después de escuchar. No usar pitch como sustituto de cadencia.
- Emitir en posición del pie si está disponible y estable; respaldo en base de la entidad, nunca en la cámara.
- Atenuación consistente para habitaciones pequeñas; evitar sumar varias curvas de proximidad que hagan inaudibles los pasos útiles o excesivos los cercanos.
- Recortar o compensar silencio inicial para que el transitorio audible coincida con apoyo. Conservar originales; cualquier edición de medios debe quedar identificada.

## 6. Eventos ambientales y ventanas de silencio

Propuesta: director ambiental autoritativo en host, con configuración de cuotas, espera, puerta elegible y prioridades. Música/ambiente continuo no cuentan como evento que reinicie inactividad, porque impedirían cualquier disparo. Sí cuentan amenazas y señales relevantes: entrada/persecución/ataque, libro activo, secuencias de muerte y otro evento ambiental. Decidir explícitamente si pickups/avisos de batería reinician la ventana; propuesta inicial: sí para avisos importantes, no para cada interacción menor.

- [ ] Al quedar el juego sin eventos relevantes activos, sortear una espera de 5–10 s **una sola vez por ventana**, no por frame.
- [ ] Reiniciar la espera ante evento relevante nuevo. No usar sólo tiempo desde último sonido si una amenaza sigue activa en silencio.
- [ ] Al cumplir espera, considerar el evento y sus cuotas/probabilidades; no disparar automáticamente cada 5 s.
- [ ] Golpes: elegir presupuesto de 1 o 2 por noche; seleccionar una sola variante en cada ocurrencia.
- [ ] Manija: evento independiente con cadencia/cuota propia por definir; no encadenar golpes y manija como una pista compuesta.
- [ ] Posicionar en una puerta real del mapa habilitado. Si no hay puerta elegible, omitir/postergar, nunca inventar una posición fuera del ambiente.
- [ ] Por defecto no solapar golpes/manija entre sí ni con entrada real. Si comienza una amenaza durante el clip, atenuar/cortar ambiente con fade para mantener legible la señal crítica.
- [ ] Aplicar las reglas de silencio conservadoramente a todo el equipo al inicio. No relajar automáticamente en multijugador: la fuente exige inactividad y una falsa alarma no debería tapar la tarea de otro jugador.
- [ ] Pausar scheduler con pausa, tracking no fiable o sesión no iniciada; reiniciar cuotas sólo al comenzar una nueva noche. Sin ráfagas tras recuperar tracking o cargar escena.

El ambiente es una falsa alarma de tensión: no debe crear Sorken ni modificar por sí solo daño, cordura o estado de puerta. Puede compartir ubicación física, pero debe diferenciarse del aviso de una entrada real lo suficiente para no volver arbitraria la respuesta.

## 7. Multijugador

- Eventos ambientales compartidos: host decide tipo, variante, puerta, secuencia y momento; clientes reproducen una vez. Evitar tiradas independientes que generen puertas distintas para cada jugador o dupliquen la reproducción del host.
- Entrada real: deduplicar por ID de entidad e intento de entrada; no por el simple retorno al estado de persecución.
- Pisadas: preferir emisión local sincronizada con la animación que cada cliente está viendo. Auditar si la replicación actual transporta fase/ciclo: estado replicado por sí solo no garantiza sincronía temporal entre peers.
- Si se exige coincidencia entre dispositivos, sincronizar época/fase/ciclo y ritmo, más semilla de variantes; render y audio deben seguir esa misma referencia. Evitar mandar un paquete independiente por cada pisada salvo necesidad demostrada.
- En incorporación tardía, continuar desde fase actual y no reproducir contactos anteriores. Recalibración, jitter y correcciones de posición no equivalen a pasos.
- No romper el filtro de sonidos individuales de Arbmos ya existente en el watcher.

## 8. Trabajo y validación del implementador

### Extensión acordada: locomoción y emergencia de Veleth (2026-10-13)

Usar [Crawling on a Wooden Floor.wav — krnash](https://freesound.org/people/krnash/sounds/389794/) para apoyos/arrastres de locomoción y para la salida del libro. Segmentar el audio, con perfiles propios por animación y velocidad efectiva, reutilizando el criterio de sincronía de Sorken. No ejecutar el archivo completo como loop fijo ni copiar cadencia bípeda.

En emergencia, sincronizar con brazos y torso aunque el root esté quieto. En persecución, los roces siguen apoyo y traslación real. Veleth se escucha espacialmente en todos los peers, a diferencia del audio individual de Arbmos. Evitar duplicar sonidos actuales de invocación/persecución. Ya hay cinco apoyos y cinco roces preparados en `output/veleth-arrastre`; rutas absolutas, duraciones y cortes en `docs/REGLAS-SONIDOS-AMBIENTALES.md`. Perfil, mezcla e integración pendientes. Las capas vocales de esfuerzo y tensión de salida aún no fueron seleccionadas.

### Extensión acordada: pasos de Arbmos (2026-10-13)

Incluir pisadas durante persecución normal y letal de Arbmos con las mismas reglas de sincronía que Sorken. Compartir infraestructura de contactos y reproducción cuando sea conveniente, manteniendo perfiles de animación y configuración de mezcla propios. No reutilizar los 12/5 apoyos de Sorken ni su cadencia. El banco inicial está preparado en `output/arbmos-pasos`, con cuatro copias exactas de Sorken para probar en una pista independiente. Un timbre distinto para Arbmos aún no fue elegido; no asumir una descarga pendiente para el banco inicial.

Aplicar detección de apoyos, variación sin repetición inmediata, emisión espacial, limpieza y deduplicación. Arbmos sólo suena al dueño de la aparición: las copias ocultas del host no emiten audio. El apagado de luz termina la resonancia según su regla, pero no cancela pisadas si el ataque ya comprometido sigue avanzando. Probar normal/letal, velocidad efectiva, inmovilidad, bloqueo de ruta, transiciones y clientes. Ver detalles en `docs/REGLAS-SONIDOS-AMBIENTALES.md`.

1. Auditar reglas, clips, referencias y escenas actuales. Escuchar los ocho WAV y medir inicio audible, duración y nivel relativo.
2. Mapear apoyos viendo entrada, persecución, cobertura y retirada; verificar rig y tiempos realmente usados en prefabs.
3. Implementar perfiles/detector y reemplazar el temporizador antiguo, usando catálogo y motor existentes.
4. Implementar scheduler ambiental y disparo exacto de crujido, con reproducción compartida sin duplicación.
5. Migrar referencias y calibrar mezcla en ambientes pequeños con auriculares y altavoz de dispositivo.
6. Probar estados, red, FPS, pausas y reinicios; documentar resultados y decisiones pendientes.

| Prueba | Criterio de aceptación |
|---|---|
| Pisadas en entrada y locomoción | Un sonido por apoyo definido; ninguna concatenación de los cuatro WAV |
| Velocidades lenta/normal y multiplicador DEV | Ritmo visual y audible coherente, sin aceleración artificial por cercanía |
| Marcha cubierta | Pasos lentos presentes y sincronizados |
| Quietud, tolerancia y obstáculos | Ningún tren de pasos sin apoyos visibles |
| Cross-fade, reinicio y wrap de loop | Sin duplicados ni ráfagas |
| Crujido por puerta | Una vez al iniciar entrada real; nunca al spawn de espera, ventana o retorno de cobertura |
| Golpes | Una variante por evento, hasta presupuesto de 1–2; sin forzar si no hay ventana |
| Manija | Independiente de golpes, con espera de inactividad válida |
| Amenaza activa y música continua | La amenaza bloquea falso evento; la música sola no bloquea toda la noche |
| Puerta inexistente / mapa irregular | No inventar emisor exterior |
| Host y clientes / incorporación tardía | Eventos compartidos sin dobles disparos; pasos alineados con animación visible |
| FPS bajo, latencia, tracking y pausa | Sin pasos atrasados ni eventos acumulados al recuperar |
| Audio anterior | Sin duplicación de sonidos reemplazados; otras pistas conservadas |

Propuesta de medición: error entre contacto visible y transitorio audible, objetivo inicial ≤80 ms en dispositivo (validar viabilidad y latencia de salida); conteo de contactos/sonidos por ciclo; duplicados = 0; motivos de postergación ambiental; cuota por noche; coste y voces simultáneas. Probar lógica con tests y sincronía/mezcla con revisión audiovisual real. No declarar sincronía basándose sólo en el contador.

Entrega del agente: tabla de referencias reemplazadas, archivos modificados, perfiles de contacto y parámetros, decisiones de ventana/cuota de manija, pruebas en host/cliente y dispositivo, y limitaciones. No borrar assets antiguos sólo para limpiar; dejar reversión sencilla.

## Salida orgánica de Veleth — selección confirmada

Usar `output/veleth-emergencia/veleth_salida_libro_organica_8_3s.wav`, derivado preparado de Alien is crawling-003 — tosha73, como complemento del banco de apoyos/roces. Dura 8,3 s según el prefab actual: inicia al comenzar la emergencia y finaliza al comenzar la persecución. Si cambia la duración efectiva, adaptar la edición sin alterar gameplay. Retirar primero el sonido antiguo de salida; conservar los susurros. No hacer loop ni continuar esta capa al perseguir. Rutas, licencia CC BY 4.0, edición y sincronización detalladas en el documento principal y en `output/veleth-emergencia/ATRIBUCION-Y-EDICION.md`.

## Vocalizaciones de Veleth — selección confirmada

- Captura fatal: `output/veleth-vocalizaciones/veleth_captura_fatal_scream.wav`, Demonic Woman Scream — nick121087, 4,458 s completos. Una vez al resolver muerte por captura de Veleth; deduplicar por víctima/evento. Preservar grito al pasar al stinger de derrota; no bloquear UI ni reiniciar fondo.
- Persecución: `output/veleth-vocalizaciones/veleth_persecucion_espectral_loop.wav`, Ghost Monster Scream — NachtmahrTV, ciclo preparado de 14,628 s (fuente 15,378 s, crossfade 750 ms). Capa espacial adicional desde Veleth, una voz por entidad/dispositivo, complemento del arrastre dinámico. No usarla como reloj de pasos, ni durante emergencia.
- Al capturar, atenuar la capa espectral para la víctima; limpiar/salir durante pausa de agarre y reactivar sin duplicados si continúa persiguiendo a otros jugadores vivos. Despawn/fin de noche limpian la voz. La muerte local no silencia globalmente amenazas de otros jugadores.
- Fuentes HQ descargadas y WAV mono PCM24/48 kHz preparados, licencia CC0 de ambas fichas y edición en `output/veleth-vocalizaciones/ATRIBUCION-Y-EDICION.md`. Reglas completas y rutas absolutas en el documento principal. Audición y calibración de mezcla en dispositivo pendientes; no se modificó código.

## Captura fatal de Sorken — audio seleccionado y preparado

Usar `output/sorken-vocalizaciones/sorken_captura_fatal_scream.wav` (2,747 s), Monster scream 1 — giddster. Una vez por captura fatal autoritativa de Sorken, deduplicada por víctima/evento. Auditar y sustituir efectos antiguos equivalentes para evitar dobles screams; conservar entrada y pasos. Preservar grito al pasar al stinger de derrota, sin bloquear pantalla ni interacción; Dark Choir Singing continúa sin reiniciarse. Fuente HQ descargada, WAV mono PCM24/48 kHz preparado, CC0 y edición registradas en `output/sorken-vocalizaciones/ATRIBUCION-Y-EDICION.md`. Reglas completas y ruta absoluta en documento principal; mezcla/animación por validar en dispositivo. No se modificó código.

## Sorken — reacción de cobertura y voces de marcha cubierta

Fuente elegida: Zombie/Monster roars, growls, grunts (juicy) — umnachtung; HQ descargado, siete clips preparados en `output/sorken-cobertura`. Dos primeras vocalizaciones en `sorken_cubrirse_dolor_enfado_01.wav` y `02.wav`, 3 s cada una ajustados sin pitch: alternar entre intentos, una sola al primer `CoverStarting`, conforme a `_coverStartPlayedThisAttempt`. Duración sigue animación/fase, no gobierna gameplay. Cinco vocalizaciones de 9–23 segundos de la fuente en `sorken_marcha_cubierta_vocal_01.wav` a `05.wav`, sólo para `CoverWalking`, independientes de cadencia de pasos. No superponer vocalizaciones de la misma entidad; cancelar y salir suavemente al cambiar de estado. No apilar sobre captura ni reiniciar fondo. Propuesta de silencios vocales, cortes, rutas absolutas y validación en documento principal. Licencia CC BY 4.0 y edición en `output/sorken-cobertura/ATRIBUCION-Y-EDICION.md`. Preparación técnica completa; audición/calibración en dispositivo e integración pendientes. No se modificó código.
