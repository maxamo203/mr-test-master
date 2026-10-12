# Mortuorium — Implementación de sonidos ambientales y pasos dinámicos de Sorken

Fecha: 2026-10-12. Guía para otro agente. Estado: diseño pendiente de implementación.

## 1. Encargo y alcance

El usuario recopiló sonidos en `docs/REGLAS-SONIDOS-AMBIENTALES.md` y quiere incorporarlos, reemplazar los sonidos equivalentes existentes y crear pasos dinámicos de Sorken: **un sonido por apoyo, elegido entre las cuatro pisadas nuevas, siguiendo la velocidad y la animación real**.

En esta entrega sólo se documenta. No se modificaron scripts, escenas, prefabs, clips ni catálogo. Este documento no autoriza iniciar implementación por sí solo: el agente implementador debe recibir la instrucción del usuario de ejecutar el trabajo.

Fuente principal de reglas: `docs/REGLAS-SONIDOS-AMBIENTALES.md`. Contexto de gameplay: `docs/GAMEPLAY-REFINAMIENTO-PENDIENTES.md` (avance muy lento bajo luz, tolerancia de 2 s, espacios pequeños y reparto de eventos multijugador). Las propuestas aquí identificadas como tales necesitan validación; no tratar cifras antiguas del gameplay como implementación vigente sin revisar el código actual.

## 2. Assets nuevos y comportamiento requerido

| Grupo | Assets | Disparo y regla |
|---|---|---|
| Golpes de puerta | `Assets/Audio/Clips/Ambiente/golpes_puerta_01.wav`, `golpes_puerta_02.wav` | Evento aleatorio de tensión, 1–2 veces por noche; después de 5–10 s continuos sin eventos; elegir una variante, nunca las dos juntas |
| Manija | `Assets/Audio/Clips/Ambiente/manija_puerta_01.wav` | Evento aleatorio independiente; misma condición de 5–10 s de inactividad; no emparejarlo automáticamente con golpes |
| Entrada por puerta | `Assets/Audio/Clips/Sorken/entrada_puerta_crujido.wav` | Una vez cuando Sorken comienza efectivamente a entrar por puerta |
| Pisadas | `Assets/Audio/Clips/Sorken/paso_entrada_puerta_01.wav` a `paso_entrada_puerta_04.wav` | Cada archivo contiene una pisada; una variante por apoyo, sin concatenar las cuatro |

Los siete WAV y sus `.meta` existen en las carpetas revisadas. No se audicionaron ni se midieron duración, silencios, calidad o intensidad en esta tarea. El implementador debe inspeccionarlos antes de calibrar.

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

1. Auditar reglas, clips, referencias y escenas actuales. Escuchar los siete WAV y medir inicio audible, duración y nivel relativo.
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
