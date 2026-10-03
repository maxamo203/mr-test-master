# Arbmos: cambio de gameplay basado en luz y cordura

Fecha: 2026-09-17.
Estado: especificación para implementar en otra rama. Este documento no implementa cambios.
Base: conversación de diseño y lectura del checkout local del proyecto.
Prioridad: sustituir la mecánica de quietud por decisiones de iluminación, manteniendo a Arbmos como alucinación individual.

## 1. Alcance y autoridad de las decisiones

Las reglas de la sección 2 fueron propuestas explícitamente por el usuario. Los detalles de la sección 3 son recomendaciones de implementación y balance surgidas en la conversación; no deben confundirse con requisitos confirmados. Se pueden usar como configuración inicial de prototipo, dejando documentadas las elecciones.

No rediseñar el modelo, rig ni animaciones como parte de este trabajo. Integrar los estados nuevos con los recursos existentes. No crear mecánicas adicionales de caras, ritual de rescate o recuperación de cordura sin ampliar el alcance.

## 2. Reglas solicitadas

- Apagar la linterna deja de consumir cordura. Eliminar el drenaje pasivo causado por oscuridad.
- La luz normal/tenue consume poca batería de forma continua y no sirve para castigar o repeler entidades.
- Incorporar una luz extra brillante que consume más batería y sirve para defenderse.
- Cuando aparece Arbmos, apagar la luz permite esconderse y conseguir que se vaya.
- Quedarse quieto deja de invocarlo o castigarte. Tampoco es obligatorio permanecer quieto para defenderse.
- Mirar directamente a Arbmos con la linterna encendida provoca una persecución y un screamer, con una pérdida importante de cordura. Ese ataque normal no provoca muerte ni derrota directa.
- Al llegar a cero de cordura se interrumpen los sonidos del juego, se escucha una música tenebrosa distintiva y aparece Arbmos para perseguir al jugador hasta matarlo.
- La fase letal se dispara por llegar a cero, sin esperar otra ventana de quietud o una nueva tirada de aparición.

La pérdida de cordura del ataque normal puede llevar a cero: en ese caso termina el golpe no letal y empieza la secuencia final. No llamar a la muerte directamente desde el golpe normal.

## 3. Propuesta de funcionamiento para el primer prototipo

### 3.1 Tres modos de linterna

| Modo | Uso | Batería | Defensa |
| --- | --- | --- | --- |
| Apagada | Ocultarse de Arbmos | Sin consumo | Ninguna |
| Tenue | Explorar y localizar recursos | Consumo bajo por segundo | No repele |
| Intensa | Defenderse de Sorken y proteger el libro | Consumo alto por segundo | Habilita defensa |

Recomendación: conservar encendido/apagado y agregar una acción de mantener para intensidad alta; al soltar, regresar a tenue si sigue encendida y tiene carga. Definir equivalencias para pantalla táctil y gamepad. Apagar tiene prioridad sobre mantener intensidad. Mostrar el modo y consumo claramente.

No hay valores de consumo aprobados. Exponer ambos consumos y ajustarlos contra duración de noche, disponibilidad de pilas y tiempo necesario para defenderse. Con batería agotada el estado efectivo siempre es apagado, incluida la red.

### 3.2 Encuentro normal de Arbmos

Secuencia propuesta:
Dormido → Presencia → Advertencia por exposición → Ataque no letal → Retirada → Espera.

- Programar apariciones con intervalos y variación por noche, independientemente de la quietud. Mantener inicialmente las noches de producción 4–6; no se pidió cambiar su introducción.
- Una señal breve anuncia su presencia y permite reaccionar.
- Con Arbmos presente, acumular tiempo de ocultación solo mientras la linterna propia está apagada. El jugador puede caminar, girar o quedarse quieto.
- Completar ese tiempo provoca su retirada. Reencender antes de completarlo reinicia el contador, como valor inicial propuesto.
- La exposición debe significar cámara/haz dirigido hacia la cabeza, dentro de alcance y sin obstrucción. No usar seguimiento ocular real.
- Luz tenue e intensa pueden provocarlo. Propuesta opcional: la intensa acumula exposición más rápido.
- Una advertencia visual/sonora precede al ataque para tolerar barridos breves accidentales.
- Propuesta: romper la exposición antes del umbral reinicia su acumulación.
- Al superar el umbral se compromete un ataque breve: persigue, ejecuta un screamer, aplica un único golpe de cordura y se retira. Propuesta: apagar ya no cancela el ataque comprometido.
- No dejar al jugador en una persecución normal infinita si no hay ruta. Definir un timeout y una salida audiovisual controlada; no convertir un fallo de navegación en muerte.
- Darle la espalda sin apagar evita una nueva exposición directa, pero no debe contar como completar la ocultación.
- Si ya estaba a oscuras cuando apareció, debe poder completar la ocultación sin exigir un encendido artificial.

Valores sugeridos, NO aprobados como balance final:
- Ocultación: aproximadamente 3 segundos.
- Exposición antes del ataque: 0,7–1 segundo.
- Golpe de cordura: 25–35 puntos sobre un máximo de 100.
- Duración exacta de presencia, límite de ataque y cooldown: configurar y evaluar en partida. No conservar automáticamente el despawn fijo antiguo de seis segundos si permite ignorar la nueva defensa.

### 3.3 Cordura cero y secuencia final

Secuencia propuesta:
Cero detectado → Silencio local → Música exclusiva/aparición → Persecución letal → Muerte.

- Disparar una sola vez por jugador vivo y noche, desde cualquier origen que pueda reducir la cordura a cero.
- Tiene prioridad sobre espera y encuentros normales. Evitar dos instancias o dos screamers superpuestos.
- Separar el golpe que agotó la cordura de la entrada musical final.
- Apagar, cambiar intensidad, mirar hacia otro lado o quedarse quieto no cancela la fase final.
- Recomendación: secuencia relativamente corta, legible e inevitable, sin una carrera arbitrariamente prolongada.
- Los tiempos de silencio, música, aparición y ataque quedan configurables.
- Usar la infraestructura existente de muerte individual y resolución de fin de noche; morir un jugador no debe forzar una derrota global si las reglas actuales permiten que sigan los demás.
- Mantener inicialmente la ausencia de recuperación de cordura. No se pidió agregar curación.

## 4. Integración con Sorken, libro y Veleth

Hechos del checkout revisado: Sorken se repele con iluminación; el libro se salva con iluminación; Veleth aparece al perderse el libro y no responde a la linterna.

Recomendación de integración:
- Sorken: exigir modo intenso tanto para repeler su entrada como para frenar/repeler su persecución. Auditar todos los caminos de defensa para que la luz tenue no siga frenándolo indirectamente.
- Libro: exigir modo intenso para frenar/revertir su oscuridad. Mantener la cooperación de varias linternas, contando solo las que defienden válidamente.
- Veleth: conservar inmunidad a ambos modos. Su defensa sigue siendo salvar el libro antes de invocarla.
- Las decisiones sobre libro e inmunidad de Veleth son recomendaciones de coherencia; el usuario no detalló sus reglas nuevas por separado.

Coordinar encuentros:
- En solitario, evitar apariciones de Arbmos superpuestas al objetivo que hay que iluminar obligatoriamente. Dejar al menos una respuesta viable antes de combinar amenazas.
- No resolverlo desactivando permanentemente la concurrencia: probar apariciones laterales, avisos y separación temporal.
- En cooperativo, un compañero puede cubrir Sorken o el libro mientras la víctima apaga.
- Solo la luz del dueño debe provocar a su Arbmos. Otro jugador no ve esa alucinación y no debe activarle castigos accidentalmente.
- Verificar que permanecer toda la noche a oscuras no sea óptimo: la exploración y las defensas deben exigir usar luz. No reintroducir drenaje pasivo de cordura como atajo de balance.

## 5. Cambios técnicos y archivos de referencia

Rutas relativas a la raíz del proyecto; inspeccionar su versión en la rama de destino antes de editar.

| Sistema | Archivos principales | Trabajo |
| --- | --- | --- |
| Linterna | Assets/Flashlight.cs; Assets/FlashlightHUD.cs; Assets/Bateries/FlashlightToggleAction.cs | Modos, consumos, controles, HUD, agotamiento y reset |
| Consulta de iluminación | Assets/Gameplay/PlayerLights.cs | Distinguir iluminación visible de iluminación defensiva; permitir consultar dueño/modo |
| Red del jugador | Assets/Network/NetworkMessages.cs; Assets/Network/NetworkManager.cs | PlayerPoseMsg actualmente transmite FlashlightOn como bool; extender para el modo efectivo |
| Cordura | Assets/Gameplay/SanitySystem.cs; Assets/Gameplay/LocalSanity.cs | Quitar drenaje por luz apagada; conservar autoridad del servidor y notificar cruce a cero |
| Arbmos | Assets/Entities/Arbmos/ArbmosDirector.cs | Reemplazar quietud, movimiento y daño continuo por exposición, ocultación, golpe único y fase final |
| Estado/presentación | Assets/Entities/Arbmos/ArbmosEntity.cs; ArbmosNetwork.cs; ArbmosAnimator.cs, en la misma carpeta | Replicar estados y eventos; mapear clips existentes sin asumir que Running ya equivale al nuevo ataque |
| Sorken | Assets/Gameplay/GameDirector.cs | Filtrar toda defensa por modo intenso |
| Libro | Assets/Gameplay/Libro/RitualBookDirector.cs | Contar luces defensivas intensas |
| Veleth | Assets/Entities/Veleth/VelethDirector.cs | Verificar inmunidad y coordinación de secuencias, preservando su comportamiento propio |
| Audio | Assets/Audio/AudioManager.cs; AudioEventWatcher.cs; AudioCatalog.cs | Avisos, screamer no letal, música final y supresión local del resto del audio |
| Tensión/efectos | Assets/Gameplay/TensionSystem.cs; Assets/Entities/Arbmos/ArbmosDistortionHUD.cs; ArbmosSmokeAura.cs | Reflejar fases nuevas, sin seguir infiriendo drenaje por movimiento |
| Configuración | Assets/Gameplay/NightConfig.cs; Assets/Gameplay/Nights/prod y dev | Parámetros nuevos, migración de assets y retiro de parámetros obsoletos |
| Reinicios/muerte | Assets/Gameplay/NightTransition.cs; ServerDeaths.cs; LocalDeath.cs | Limpieza completa de secuencias, sonido y estados |
| Debug | Assets/Gameplay/ArbmosDebug.cs; Assets/Entities/Arbmos/ArbmosQuietudViz.cs | Retirar controles de quietud; mostrar exposición, ocultación y fase |

Precauciones específicas:
- PlayerLights.Alcanza actualmente comprueba cono y distancia, no oclusión. No afirmar que ya evita detección a través de paredes. Integrar la comprobación con la geometría de escaneo y sus capas.
- Usar el cono visible correspondiente a cada modo; evitar divergencia entre parámetros de NightConfig y linterna.
- El servidor decide aparición, exposición válida, daño, transición letal y muerte. Los clientes presentan su copia dirigida.
- Actualizar serialización, deserialización, almacenamiento y todos los emisores/receptores del modo de luz. Definir compatibilidad de protocolo o exigir misma versión.
- Un bool derivado puede mantenerse para compatibilidad interna, pero no debe seguir habilitando defensa con cualquier luz encendida.
- No duplicar daño o audio por snapshots repetidos: cada ataque debe tener identidad o una transición consumible una sola vez.
- El silencio final es local a la víctima y debe cubrir sonidos ya activos y sonidos que intenten empezar después. No usar volumen global cero que silencie también la música exclusiva.
- No silenciar comunicación externa/voz como parte de este cambio.
- Restaurar audio y limpiar temporizadores al morir, reintentar, cambiar de noche, desconectarse o volver al menú.
- La persecución actual cae a movimiento directo cuando falla la ruta: revisar este caso para no atravesar paredes.
- Conservar configuraciones específicas DEV: no copiar producción indiscriminadamente. En la revisión, DEV noche 6 tiene Arbmos desactivado.

## 6. Orden recomendado de implementación

1. Revisar cambios existentes en la rama y fijar los parámetros/controles del prototipo.
2. Implementar modos de luz y transmisión por red, con pruebas de batería y reinicio.
3. Migrar defensas de Sorken/libro y quitar drenaje de cordura por oscuridad.
4. Sustituir la máquina de estados de Arbmos y conectar detección/ocultación.
5. Integrar golpe no letal, cruce a cero, secuencia audiovisual y muerte.
6. Migrar configuración, HUD, debug y textos que todavía indiquen quedarse quieto.
7. Probar individual, host/cliente y concurrencia de amenazas; ajustar balance después.

## 7. Criterios de aceptación

- [ ] Luz apagada no reduce cordura, aunque se prolongue la oscuridad.
- [ ] Quedarse quieto no provoca apariciones ni daño; caminar no drena cordura por sí mismo.
- [ ] Tenue e intensa consumen a sus tasas configuradas; apagada no consume; batería nunca negativa.
- [ ] Sin carga no hay iluminación ni defensa efectiva, también para jugadores remotos.
- [ ] Tenue no repele ni frena a Sorken ni salva el libro bajo la integración propuesta.
- [ ] Intensa defiende Sorken/libro; ninguna intensidad repele Veleth.
- [ ] Ocultarse apagando retira a Arbmos sin exigir inmovilidad.
- [ ] Barrido menor al umbral no causa golpe; exposición suficiente sí.
- [ ] Mirarlo apagado, fuera de alcance o detrás de una pared no provoca ataque.
- [ ] Cada ataque normal quita cordura exactamente una vez y no invoca muerte directamente.
- [ ] Llegar a cero dispara exactamente una secuencia final sin otra invocación por quietud.
- [ ] La fase final no se cancela con luz ni movimiento; completa la muerte prevista.
- [ ] Solo el dueño ve/oye su Arbmos y recibe su daño; otras linternas no lo provocan.
- [ ] El silencio final no afecta a compañeros ni impide escuchar su música exclusiva.
- [ ] Sorken/libro/Arbmos simultáneos dejan una respuesta comprensible en solitario.
- [ ] Reinicio, cambio de noche y desconexión no dejan música, muteos, entidades o callbacks pendientes.

Automatizar las transiciones, temporizadores, daño único y filtrado de modos cuando sea viable; validar en Play Mode/dispositivo la puntería, oclusión, audio, controles y navegación. Probar al menos host y un cliente. No dar por validado el balance solo por compilar.

## 8. Traspaso a otra rama/agente

El checkout de origen contiene modificaciones y recursos nuevos de otras tareas, incluidos Arbmos, Veleth y red. Esta documentación no los modifica ni los incorpora a un commit.

Antes de implementar en otra rama, comprobar que contiene la versión de gameplay y los recursos que se quieren conservar. Una rama basada únicamente en HEAD puede no incluir cambios locales pendientes. Transferir este documento explícitamente si no está versionado; no asumir que aparece en un worktree nuevo.

Prompt sugerido:
> Implementá docs/ARBMOS-GAMEPLAY-LUZ-IMPLEMENTACION.md en esta rama. Respetá los requisitos confirmados y distinguí las recomendaciones de balance. Inspeccioná primero el estado actual y conservá los cambios existentes de Arbmos, Veleth y red. Aplicá la migración completa de linterna, cordura, defensas, encuentros, audio y configuración; verificá host/cliente y reinicios. Documentá los valores elegidos, las pruebas realizadas y cualquier decisión de diseño que quede pendiente.

