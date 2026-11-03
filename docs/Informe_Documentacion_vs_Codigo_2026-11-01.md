# Informe: documentación (revisión 18/10/2026) vs. implementación

**Documento revisado:** `docs/Grupo112_Documentacion_revision_2026-10-18_anexos_I_II_III.docx` (texto completo, tablas, criterios, casos de prueba CP/IT, anexos y comentarios de revisión).
**Código revisado:** rama `main` @ `b2af40f` + cambios sin commitear (`Nights/dev|prod/Noche 1.asset`), ~60 k líneas de C#, escenas, assets de noches, `ProjectSettings`, `Packages`, plugins nativos.
**Fecha:** 01/11/2026.

> **Alcance del análisis.** Es una revisión **estática** (lectura de código, escenas y assets). No ejecuta el juego ni valida en dispositivo, así que "implementado" significa "el código existe y está cableado", no "funciona en el teléfono". El propio documento ya distingue *implementación observada* de *validación*; este informe se enfoca sólo en la primera.

Cada hallazgo indica **qué dice el documento**, **qué hace el código** y la **evidencia** (archivo:línea). La columna *¿Dónde corregir?* es una sugerencia; la decisión queda para el equipo.

---

## 0. Resumen ejecutivo — lo más importante

| # | Hallazgo | Tipo |
|---|---|---|
| 1 | **Composición de noches PROD no respeta "Sorken N1‑N3, Sorken+Veleth+Arbmos N4‑N6"**: N2 y N3 tienen el libro activo (perderlo invoca a Veleth antes de N4); N4 no tiene Sorken ni libro (sólo Arbmos); N6 anuncia "siete minutos" y dura 300 s. | Doc ≠ código (datos) |
| 2 | **US‑1.4 / 10.1 piden un "gate de SO" (Android API 34 / iOS 17)** que el código **eliminó a propósito** (decisión registrada en `CLAUDE.md`: sólo aviso no bloqueante por falta de ARCore). El doc nuevo reintroduce lo que se había decidido sacar. | Conflicto de decisión |
| 3 | **No hay pantalla de créditos.** La licencia CC‑BY 3.0 (Little Robot Sound Factory, Mike Koenig) y CC‑BY 4.0 (5 fuentes de Freesound) **obligan** a acreditar. US‑10.4 CA3 / US‑11.3 CA2 lo exigen. | Doc pide, código no tiene |
| 4 | **US‑4.4 (titileo + ruido al apuntar/acercarse a la entrada del Sorken) no está implementado**: `flickerAimAngle`/`flickerDistance` no se leen en ningún lado y la pista `sorkenApuntado` nunca se reproduce. | Doc pide, código no tiene |
| 5 | **Multijugador**: no hay tope de 4 jugadores (el 5º entra), la sala **sigue anunciándose por UDP después de iniciar la noche**, no hay ping cooperativo ni reingreso completo. | Doc pide, código no tiene |
| 6 | **US‑2.3 (recalibrar explícitamente durante la noche sin reiniciar)**: durante una noche activa no hay ninguna vía de recalibración manual (sólo en la sala de sincronización y en el escáner). | Doc pide, código no tiene |
| 7 | **US‑1.6: el doc dice que "el apagado accesible sin mando sigue pendiente", pero ya está implementado** (franja de la linterna mirando hacia arriba, zona de pausa, recoger por mirada, intensa automática). | Doc desactualizado |
| 8 | **Tolerancias de pickup**: el doc dice "servidor acepta hasta 1,5 m" (pila) y "valida a 1 m" (reliquia); el servidor suma **+0,75 m** de margen (2,25 m y 1,75 m reales). | Doc ≠ código |
| 9 | **Grandes funcionalidades sin historia de usuario**: camino de TUTORIAL de 5 pasos, tutorial del primer escaneo, puntos de anclaje (AnchorPointManager), retrato del asesino en la muerte, falsas alarmas ambientales, calibración del visor (IPD/convergencia/3D), variantes de build DEMO/COMPLETA como mecanismo técnico. | Código sin doc |
| 10 | **Arquitectura (8.2) describe mal la UI**: dice uGUI para menús/paneles; en realidad es **UI Toolkit** (Rediseño 2.0, 21 archivos) + **IMGUI** (43 `OnGUI`); uGUI sólo aparece en `MRCardboardController`. Falta también la dependencia `com.coplaydev.unity-mcp` (Git `#main`) en el manifest. | Doc ≠ código |

---

## Parte A — Lo que el documento especifica y el código **no implementa** (o implementa distinto)

### A.1 Épica 1 · Plataforma y hardware

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| US‑1.1 | "Elegir entre visor con mando, visor sin mando y teléfono en mano". | No hay selector de modalidad: se **deriva** de `USAR EL VISOR` (toggle) + si hay mando conectado (con visor y sin mando se activa `ControlMirada` sola). Funcionalmente cubre las 3, pero no hay "elección" explícita. | `Rediseno/UI/ModoVisor.cs`, `Rediseno/ControlMirada.cs:30-33`, `Rediseno/Menus/MenuOpciones2.cs:107` | Doc (redactar como modalidad inferida) o código (selector) |
| US‑1.2 CA2 | "La interfaz informa la pérdida del mando". | `GamepadManager` detecta la desconexión (`OnDisconnected`) pero **nadie lo escucha en UI**; sólo hay `Debug.Log`. El fallback (mirada/táctil) entra implícitamente. | `Gamepad/GamepadManager.cs:69,393` (sin suscriptores fuera del archivo) | Código (aviso) o doc |
| US‑1.3 CA2 | Medir latencia pose→linterna (< 100 ms) con build instrumentada. | No existe instrumentación de latencia. | — | Doc ya lo marca pendiente; falta tarea de código |
| US‑1.4 CA1/CA2, 10.1, IT‑007 | Diferenciar "mínimo instalable", **"gate de SO" (API 34 / iOS 17+)** y proveedor AR. | **No hay gate de SO.** Sólo `ARSession.CheckAvailability()` → aviso no bloqueante `SinAR` (con INSTALAR ARCORE). `ARCoreSettings.m_Requirement = 1` (Optional). El gate y `DeviceCompatibility` se **borraron por decisión confirmada** (ver `CLAUDE.md`, "Sin bloqueo por versión de SO"). | `Gameplay/NightMenuUI.cs:126-169`, `Assets/XR/Settings/ARCoreSettings.asset:15` | **Decidir**: el doc contradice una decisión ya tomada |
| US‑1.5 CA1 | Distinguir SO "desconocido" de "incompatible". | No se evalúa la versión de SO en absoluto. Sí existe el aviso de **recomendación** "MEJOR EXPERIENCIA EN iOS" (sólo Android, una vez) → CA2 cubierto. | `NightMenuUI.cs:94`, `Rediseno/Menus/AvisoMenu2.cs:101` | Doc (quitar CA1) o código |
| US‑1.6 CA3 | Navegar menús, pausa y opciones **por mirada** sin mando. | Parcial: `MENÚS QUIETOS EN EL CUARTO` (anillo de mirada) existe pero está **apagado por defecto** y su comentario dice "el modo con la mirada queda para más adelante". La pausa sí se abre mirando arriba (zona de pausa). | `UI/GameOptions.cs:178-190`, `ControlMirada.cs:22-25` | Ambos (definir default) |
| 10.1 | "Android tiene mínimo API 29 y gate API 34, iOS gate 17+ y deployment target 26.0". | `AndroidMinSdkVersion: 29`, `AndroidTargetSdkVersion: 0` (automático), `iOSTargetOSVersionString: 26.0`. No hay gate 34 ni 17. **iOS 26.0 como deployment target excluye todo iPhone con iOS 17‑25**, contradiciendo "campaña iOS 17+". | `ProjectSettings/ProjectSettings.asset:180,181,198` | Código (bajar deployment target) o doc |

### A.2 Épica 2 · Escaneo y calibración

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| US‑2.3 CA2, CP‑002 | Recalibrar **de forma explícita durante la sesión** sin reiniciar la noche. | Durante una noche activa no hay botón ni gesto de recalibración: tocar el 0,0 sólo abre el ajuste si `EnSincronizacion` (sala); la pausa no tiene RECALIBRAR/AJUSTAR; el tracking de imagen se apaga al anclar. Sólo hay corrección **automática** con `AnchorPointManager` (opt‑in "PUNTOS DE ANCLAJE"). | `AR/ARLobbyManager.cs:98-105`, `Gamepad/PauseMenuController.cs` (página Main) | Código (AJUSTAR en pausa) o doc |
| US‑2.3 "sin reconocimiento periódico no ratificado" | — | `AnchorPointManager` cambia de ancla y corrige el mapa continuamente (si el jugador activó PUNTOS DE ANCLAJE). Es exactamente un "reconocimiento periódico"; el doc no lo ratifica ni lo menciona. | `AR/AnchorPointManager.cs` | Doc (ratificarlo) |
| US‑2.7 CA3, IT‑002 | Fallo de descarga o paquete corrupto → **error recuperable visible**. | `ScanPackage.Import` devuelve `null` con `Debug.LogWarning` ("magic MSCN ausente", "JSON inválido"); no se encontró un mensaje de UI específico para el jugador. | `Scanner/ScanPackage.cs:71-97` | Código (verificar/añadir aviso) |
| US‑2.6 | Calidad de referencia + rechazo del proveedor con recaptura. | Implementado (`ReferenceImageQuality`, `ARImageAnchor.AvisoImagen` + RECAPTURAR). ✔ | `Scanner/ReferenceImageQuality.cs` | — |

### A.3 Épica 3 · Linterna, pilas y reliquias

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| US‑3.3 | Con poca batería la linterna **titila y la luz es más tenue**. | Titila (bajo 35 %) y suena aviso al 20 %, pero la **intensidad no baja** con la carga (sólo el "dip" de cada titileo). | `Flashlight.cs:82-97, 370-390, 502-508` | Doc o código |
| US‑3.5 CA1, CP‑018 | "El servidor acepta una sola recogida **hasta 1,5 m**". | Servidor: `pickupMaxDistance (1,5) + 0,75 m` = **2,25 m** con línea de vista. Cliente apunta hasta 1,5 m. | `Bateries/BatterySpawnManager.cs:507`, `SampleScene.unity:1245` | Doc (registrar margen) o código |
| US‑12.1 CA2, CP‑104 | Reliquia: cliente 2,5 m, **servidor 1 m**. | Servidor: `1 + 0,75` = **1,75 m**. Cliente 2,5 m (default, no sobreescrito en escena). La asimetría sigue existiendo pero con otros números. | `Collectibles/CollectibleSpawnManager.cs:416`, `SampleScene.unity:151` | Doc |
| US‑3.6, CP‑019 | Espera por punto y cupo **"pendientes de decisión de balance"**. | Ya hay valores: `respawnSeconds = 30` dividido por `NightConfig.batterySpawnRateMultiplier`; un punto por cada 1,5² m² de área, máx. 12 (escena). | `BatterySpawnManager.cs:169-174, 263`, `SampleScene.unity:1242-1253` | Doc (registrar la regla vigente) |
| US‑3.9 CA3, CP‑103 | "No se crea un punto exterior inventado". | Si las paredes no forman recinto, pilas y reliquias se sortean en un **radio de respaldo de 3 m** alrededor del PISO (o del 0,0). Es exactamente un punto "inventado" fuera de un interior validado. | `BatterySpawnManager.cs:67-70`, `Gameplay/Spawning/SpawnSampler.cs:77` | **Decidir** |

### A.4 Épica 4 · Sorken

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| **US‑4.4**, CP‑125/126 | Al acercarse o apuntar al punto donde intenta entrar, **la linterna titila y se oyen ruidos**. | **No implementado.** `NightConfig.flickerAimAngle` / `flickerDistance` no se usan en ningún archivo; la pista `sorkenApuntado` del catálogo nunca se dispara. El único titileo es por batería baja. (Hay niebla negra en la entrada, `SorkenDarknessCue`, que es feedback visual pero no el descripto.) | `Gameplay/NightConfig.cs:177-179`, `Audio/AudioCatalog.cs:202` | Código o doc |
| US‑4.1 | Ruido **distinto por cada punto** de entrada. | Distinto **por tipo** (puerta/ventana), no por punto. El doc ya lo reconoce como "cobertura parcial". | `Gameplay/GameDirector.cs:333-339` | Doc ya consistente |

### A.5 Épicas 7 y 8 · Libro/Veleth y noches (datos PROD)

Valores reales de `Assets/Gameplay/Nights/prod/*.asset`:

| Noche | Sorken | Libro (→ Veleth) | Arbmos | Reliquias | Duración | Briefing |
|---|---|---|---|---|---|---|
| N1 | ✔ | ✘ *(cambio sin commitear: estaba ✔)* | ✘ | ✔ | 300 s | "El Sorken tantea los accesos…" |
| N2 | ✔ | **✔** | ✘ | ✔ | 300 s | "Insiste mas seguido…" |
| N3 | ✔ | **✔** | ✘ | ✔ | 300 s | "…el libro se apaga cada vez mas seguido…" |
| N4 | **✘** | **✘** | ✔ (forzado) | **✘** | 300 s | "Solo Arbmos te observa…" |
| N5 | ✔ | ✔ | ✔ (forzado) | ✔ | 300 s | "El Arbmos acecha mas seguido…" |
| N6 | ✔ | ✔ | ✔ (forzado) | ✔ | 300 s | "**Siete minutos**, sin tregua…" |

| ID | Documento | Código | ¿Dónde corregir? |
|---|---|---|---|
| 1.1, US‑7.2, US‑7.5 CA2, US‑8.5, CP‑042/047/099/118 | Veleth **sólo desde N4**; en N1‑N3 no se invoca. | El libro está activo en N2 y N3 y `RitualBookDirector` invoca a Veleth al perderlo **sin mirar el número de noche** (el único gate es el tutorial). Perder el libro en N2/N3 ⇒ Veleth. | Datos (apagar libro N2‑N3) o código (gate por noche) — el doc ya pide "adecuar" |
| US‑8.5 CA2, CP‑118 | N4‑N6 mantienen Sorken; "N4 sólo Arbmos no se acepta". | N4: `sorkenActive: 0`, `bookActive: 0`. | Datos |
| US‑8.5 CA2 | "Ningún parámetro resulta más favorable sin excepción aprobada". | `batterySpawnRateMultiplier`: N4 0,85 → N5 0,95 → **N6 1,05** (más pilas en N5 y N6 que en N4). N4 sin Sorken ni libro es globalmente más fácil que N3. | Datos o registrar excepción |
| US‑8.2, US‑8.6 CA1 | El briefing informa **duración y composición** y coincide con lo cargado. | Los briefings son texto libre, no informan duración; N6 dice "siete minutos" y dura 300 s; N3 menciona el libro pero no a Veleth. | Datos (textos) |
| — | `arbmosForceSpawnAfterCooldown` (tooltip: "Solo DEV"). | Está en `1` en **PROD** N4‑N6: se ignora `arbmosSpawnChancePerAttempt`. | Datos (o doc lo ratifica) |

### A.6 Épica 9 · Multijugador

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| US‑9.1 CA2, CP‑064, IT‑004 | Rechazar al 5º jugador. | No hay tope: `HandleClientConnected` acepta a cualquiera (también con la noche en curso). | `Network/NetworkManager.cs:723-772` | Código |
| US‑9.2 CA2, CP‑066 | "Una sala **iniciada** o cerrada deja de anunciarse". | `LanDiscovery` sólo se detiene en `OnDestroy` (`StopAll`). Tras INICIAR NOCHE se sigue anunciando. | `Network/LanDiscovery.cs:101,326`; único `StartAdvertising` en `GameBootstrapper.cs:228` | Código |
| US‑9.3 CA1, CP‑067 | Error recuperable cuando la búsqueda LAN no puede operar (permiso/aislamiento). | La UI muestra "Buscando salas en tu red…" indefinidamente; los fallos del socket sólo van a `Debug.LogError`. | `Rediseno/Menus/PanelUnirse2.cs:94`, `LanDiscovery.cs:165-209` | Código |
| US‑9.7, CP‑075/076 | Ping cooperativo. | No existe. (El doc ya lo marca "no acreditado".) | — | Decidir: implementar o retirar |
| US‑10.1 CA2, CP‑078 | Política de reingreso completa. | Sólo "catch‑up" de entidades al conectar; no recupera identidad/estado del jugador. (El doc lo reconoce.) | `NetworkManager.cs:743` | Decidir |

### A.7 Épicas 10 y 11 · Infraestructura, permisos, créditos, efectos

| ID | Documento | Código | Evidencia | ¿Dónde corregir? |
|---|---|---|---|---|
| **US‑10.4 CA3, US‑11.3 CA2**, R‑09 | Créditos exigibles accesibles en la app. | **No hay pantalla de créditos** (ni en menú principal ni en opciones). Con los clips CC‑BY en el build es una obligación legal. | Menús: `MenuPrincipal2`, `MenuOpciones2` (sin entrada de créditos) | Código |
| US‑10.4 CA1 | Cada permiso explica su finalidad. | Textos mínimos: cámara "Necesario para AR", micrófono "Necesario para el chat de voz". Red local: texto inyectado por `IOSBuildPostProcessor`. | `ProjectSettings.asset:621-623` | Código (textos) |
| US‑10.4 CA2, 8.3 | Explicar que **compartir el `.mscn` incluye una foto del espacio físico**. | No hay aviso previo al compartir. | `Scanner/MscnShare.cs` | Código |
| US‑10.2 | Brillo, FOV, aberración y confort "pendientes". | Correcto que no existen como opciones de usuario. Pero: aberración/intensidad VHS **sí existen como sliders DEV**, y el visor ya tiene "TAMAÑO DE LA IMAGEN" (≈ FOV/zoom) y separación de ojos para el usuario. | `PauseMenuController.cs:343-365, 461-475`, `MenuOpciones2.cs:114-120` | Doc (afinar la lista) |
| 2.5 (procedencia de audio) | Lista de fuentes y licencias. | **Falta "Dying Light Bulb — Mike Koenig" (CC BY 3.0, crédito obligatorio)**, que está en `ATTRIBUTIONS.md` y en el build (aviso de batería/agotamiento). Además las 5 fuentes CC BY 4.0 también exigen crédito; el doc sólo destaca a Little Robot Sound Factory. | `Assets/Audio/ATTRIBUTIONS.md:9-37` | Doc |
| US‑10.4 CA3 | Procedencia de **todos** los assets distribuidos. | El inventario cubre sólo audio. No hay inventario de **modelos 3D** (carpetas `Meshy_AI_…`: generados con Meshy AI — revisar términos), **fuentes tipográficas** (Bebas Neue, Special Elite, IBM Plex Mono en `Resources/Fonts`), ni del pack del libro ritual. | `Assets/Meshy_AI_*`, `Assets/Resources/Fonts/` | Doc + inventario |

### A.8 Arquitectura e instalación (capítulos 8 y 10)

| Sección | Documento | Código | ¿Dónde corregir? |
|---|---|---|---|
| 8.2 | "Unity UI (uGUI) 2.0.0 — Menús, paneles, botones, retícula e interfaces de escaneo, lobby y gameplay". | Menús/pausa/sala/fin de noche: **UI Toolkit** (`Rediseno/`, `PantallaUI`). Escáner, HUD de noche, CONTROL, briefing: **IMGUI** (`MortuoriumTheme`). uGUI: sólo `MRCardboardController`. | Doc |
| 8.2 | Dependencias directas listadas. | El manifest además incluye `com.coplaydev.unity-mcp` (desde **GitHub `#main`**, herramienta de desarrollo — riesgo de build no reproducible), `timeline`, `visualscripting`, `multiplayer.center`, `feature.development`. | Doc (o quitar paquetes) |
| 8.2 Integraciones móviles | Sólo `MscnNative.mm`. | También `Plugins/iOS/ThermalNative.mm` (estado térmico/bajo consumo, lo usa `PowerProbe`, dev). | Doc |
| 8.4 | "DisplayOnly reconstruye marcadores necesarios para gameplay". | Correcto (invisibles, como spawn del Sorken). ✔ *(Nota: `CLAUDE.md` dice lo contrario y está desactualizado.)* | `CLAUDE.md` |
| 9.1.2 | Editor "con stub de AR/**XR Simulation**". | Se usa el stub propio (`ARImageAnchor.EditorStub`), `EditorPanorama360` y `EditorPlayerControls` (WASD). Existe un `SimulationLoader.asset`, pero el flujo documentado en `CLAUDE.md` es el stub. | Doc (verificar) |
| 10.5 | Compilación iOS genérica con Xcode. | El flujo real es `./compilar-ios.sh` con **Xcode 26** (el SDK de iOS 27 rompe cámara ARKit) y export a carpetas separadas para demo/completa. | Doc |
| 10.4/10.6 | Firma, versionCode, variante. | Configurado: keystore en proyecto, alias `mortuorium-demo`, `versionCode 2`, `bundleVersion 0.2.0`. El cambio DEMO/COMPLETA y bundle id se hace con **Mortuorium > Variante de build**; el doc no explica ese menú. | Doc |

---

## Parte B — Lo que está **implementado y no documentado** (o sólo mencionado de pasada)

### B.1 Funcionalidades de jugador (candidatas a historia de usuario)

| Funcionalidad | Qué hace | Dónde | Estado en el doc |
|---|---|---|---|
| **Camino de TUTORIAL (5 pasos)** | LA LINTERNA → PILAS Y RELIQUIAS → SORKEN → ARBMOS → LIBRO Y VELETH → tarjeta final con JUGAR NOCHE 1. Encadena pasos en la misma sesión AR; progreso por dispositivo; pasos repetibles. Cada paso tiene su `NightConfig` en `Resources/Tutoriales`. | `Gameplay/Tutoriales/*`, `Rediseno/Menus/MenuTutoriales2.cs` | Sólo "tutoriales" genérico en 2.2.4. **Sin US.** |
| **Tutorial del primer escaneo** | Guía de 7 pasos que se abre sola en el primer escaneo nuevo; banner que deriva el sub‑paso del estado real; reabrible con `?`. | `Rediseno/Escaner/TutorialEscaneo.cs` | **Sin US.** |
| **Puntos de anclaje extra** (`PUNTOS DE ANCLAJE`) | Opt‑in por dispositivo: se colocan anclas antes de la noche y el mapa se corrige automáticamente según el ancla más cercana/mirada (histéresis, cooldown, tope de corrección). Panel ANCLAS en la sala. | `AR/AnchorPointManager.cs`, `Rediseno/Menus/PanelAnclas2.cs`, `GameOptions.PuntosAncla` | Sólo "ancla auxiliar" en CP‑109 y la minuta 5. **Sin US.** |
| **Ocultar «NO TENGO LA IMAGEN»** | Opción para no ofrecer calibración manual. | `GameOptions.OfrecerSinImagen`, `MenuOpciones2.cs:92` | No figura |
| **Retrato del asesino** | Al morir se muestra un encuadre de la entidad que te mató (config por entidad). | `Gameplay/RetratoAsesinoConfig.cs`, `LocalDeath.cs` | No figura (US‑4.6 sólo habla de muerte/espectar) |
| **Falsas alarmas ambientales** | Director host‑authoritative que reproduce golpes/manija en **puertas reales** del mapa, sincronizado a todos, sin crear amenazas. | `Audio/AmbientAudioScheduler.cs`, `MessageType.AmbientAudio` | No figura (sólo aparece en la lista de audio con "permiso no confirmado") |
| **Veleth suspende al Sorken y a Arbmos** | Al invocarse Veleth, el Sorken se retira y no vuelve; Arbmos se detiene. | `RitualBookDirector.cs:274-278`, `GameDirector.cs:217` | No figura en US‑7.6 |
| **Jugador sin batería no recibe amenazas nuevas** | `ThreatCoordinator` sólo cuenta como "capaz" a quien tiene carga > 0 y tracking fiable. | `Gameplay/ThreatCoordinator.cs:132-143` | US‑9.8 habla de "jugadores capaces" sin definirlo |
| **Arbmos drena cordura si la linterna está encendida en su presencia** (2/s) | Además del golpe de 30. | `NightConfig.arbmosLightSanityDrainPerSecond`, `ArbmosDirector.cs:178-184` | Implícito en CP‑094; no está como regla |
| **Selección de entrada del Sorken** | Ponderada por "sequía" de cada abertura, evita la misma entrada más de 2 veces seguidas. | `GameDirector.cs:405-432` | No figura |
| **Control por mirada completo** | Recoger mirando 1,2 s (0,45 s si estás cerca); prender/apagar cruzando una franja ~32° sobre el horizonte; pausa sosteniendo la vista ~58°; intensa automática al centrar libro/Sorken. | `Rediseno/ControlMirada.cs:9-33` | El doc lo da como pendiente (ver A.1) |
| **Control táctil tap/hold** | Tocar = acción contextual; mantener 0,5 s = intensa; soltar vuelve a tenue. | `Bateries/ContextActionController.cs:66-84` | Cubierto en CP‑089 ✔ |
| **Calibración del visor** | Zoom del feed, posición de cada ojo, **Visión 3D (estéreo)**, separación de ojos (IPD), distancia de foco. | `PauseMenuController.cs:343-365`, `MenuOpciones2.cs:105-120`, `CardboardCalibrationUI.cs` | No figura (US‑10.2 no lo lista) |
| **PAREDES VISIBLES** (pausa) y ocultado automático al iniciar | Muestra/oculta el oclusor de paredes. | `SceneOccluderMode.cs` | No figura |
| **Música de menú** (opcional, apagada por defecto) | — | `GameOptions.MusicaMenu` | No figura |
| **Voz: volumen por jugador y sensibilidad del micrófono** | Sliders en pausa > CHAT DE VOZ. | `PauseMenuController.cs:383-411` | US‑9.6 sólo menciona mute/volumen |
| **Contador LISTOS n/m en la sala**, heartbeat y timeouts en ambos sentidos | El host da de baja a clientes silenciosos (10 s); el cliente vuelve al menú con aviso si el host calla 10 s. | `NetworkConfig.cs`, `ARLobbyManager.cs` | US‑9.4 cubre el efecto, no el mecanismo |
| **Sincronización de mapas por hash** | `MapAnnounce`/`MapRequest`/`MapData`, dedupe al importar. | `NetworkManager.cs`, `ScanPackage.cs` | Cubierto en US‑2.7 ✔ |
| **Escaneo con LiDAR** (iOS) | Malla LiDAR como capa de raycast en el escáner. | `AR/LiDARScanner.cs`, `RaycastResolver` | Mencionado sólo como "puede aprovecharse" |
| **Herramientas del escáner** | PISO, PARED (polilínea), IDENTIFICAR (marcadores puerta/ventana data‑driven), AGUJERO PUERTA, CUBO, ANCLAS, AJUSTAR ORIGEN, RECALIBRAR (2 modos). | `Scanner/*`, `ReticleController.cs` | Descripto en forma genérica en 1.2/8.4 |
| **Calidad AR** (Rendimiento/Equilibrado/Calidad) | Ajusta subsistemas AR por escena (batería). | `AR/ARQuality.cs` | Mencionado en US‑10.2 ✔ |

### B.2 Mecanismos técnicos no documentados

- **Variantes DEMO/COMPLETA como define de compilación** (`MORTUORIUM_DEMO`, `BuildVariant.EsDemo` const, bundle ids separados para coexistir, FIN DE LA DEMO). El doc describe el *qué* (US‑8.8) pero no el *cómo* ni el menú del editor.
- **Teardown de sesión** (`SceneFlow.GoTo` → `NightTransition.TeardownSesion` + `ARSession.Reset()`): crítico para no acumular anclas/memoria (crash tras 5‑6 partidas). US‑8.7 CA2 lo nombra pero no lo explica.
- **Ticks de red en tiempo no escalado** (necesario porque la muerte global pone `timeScale = 0`).
- **Audio**: catálogo único `AudioCatalog` en Resources, `AudioManager` como único creador de fuentes, `AudioEventWatcher` central, 48 voces reales, prohibición de tocar `AudioSettings` en runtime (rompe la voz). Nada de esto está en el capítulo 8.
- **Codec de voz propio (ADPCM, ~30 ms)** sobre el TCP del juego.
- **Telemetría de gameplay** (`GameplayTelemetry`): eventos estructurados por `Debug.Log` (no se envían fuera del dispositivo). **No está gateada a development build**, así que también loguea en release (por evento, no por frame).
- **Arquitectura de tiers** (editor‑only / development build / release) y su regla de rendimiento — central en `CLAUDE.md`, ausente en 8.5.

### B.3 Herramientas de desarrollo (sólo dev/editor — probablemente no necesitan US, pero sí mención en 8.5 / 10.3)

Debug HUD y paneles (`DebugHud/`), `PowerProbe` + `ThermalNative.mm`, noches DEV y "NOCHES DE PROD" (`PlayDataProfile`), BORRAR PROGRESO / DESBLOQUEAR TODAS, ARBMOS (DEV), FRANJA LINTERNA (DEV), VELOCIDAD ENTIDADES (DEV), sliders de linterna y VHS (DEV), PISO AUTOMÁTICO (dev), ARMADO RÁPIDO de prácticas sin escaneo (dev), `LogMuerteDev` (COPIAR LOG), `EditorPanorama360`, `EditorPlayerControls` (WASD), menú de splash e ícono, herramientas de **banners promocionales** (`Assets/Promo/`, editor‑only), ventanas de setup de animaciones/prefabs, escenas de prueba (`EntityModelsReview`, `RitualBookVelethTest`, `SorkenDoorGameplayTest`) y `Sorken/AnimationReviewLocal/`.

### B.4 Código legado o inactivo (ni documentado ni en uso real — candidato a borrar o a mencionar)

| Componente | Situación |
|---|---|
| `CloudAnchorHost` / `CloudAnchorResolver` | Habilitados en `SampleScene`, pero **nadie llama** `StartScanning`; el origen compartido es la imagen del mapa. Inertes. |
| `Sorker` / `SorkerAI` / `PlayerEntity.Health` / golpe en `PlayerNetwork` | Combate cuerpo a cuerpo del sample viejo; `SorkerAI` sigue en el prefab de un modelo Meshy. `SorkerNav` (A*) **sí** se usa para el Sorken. |
| `AutoWallBuilder` (PARED AUTOMÁTICA BETA) | WIP, `AutoWallScanBetaEnabled = false`, no está en ninguna escena. |
| `IOSImageAnchor`, `IOSARBootstrap` | No referenciados en escenas ni prefabs. |
| `GyroscopeTracking`, `CubeAnchorDrag`, `ARPlaneOccluder` | Cableados en prefab/escena pero ajenos al flujo documentado. |
| `SceneNavUI`, `RecalibrateButton`, `NetworkDebugUI` | Cáscaras vacías obsoletas (ya señaladas en `CLAUDE.md`). |
| Archivos no‑código dentro de `Assets/` | `Assets/CONAIISI/` (papers de otros grupos y `Grupo112_Documentacion_v9.0.docx`), `ConceptArt/`, `_Recovery/`, el `.docx` nuevo y `Documentacion Mortuorium v9.0.md`: Unity los importa como assets (no entran al build, pero ensucian el proyecto). |

---

## Parte C — Inconsistencias internas del documento

1. **Criterios duplicados por copiar/pegar**: US‑2.3 CA1 y US‑2.4 CA1 son idénticos a US‑2.2 CA2 ("Existe un mapa guardado o importable… se carga, realinea…"). CP‑010, CP‑011 y CP‑012 son el mismo caso con distinta evidencia.
2. **US‑3.3 CA1** es una copia del criterio de US‑3.1/CP‑015, no habla del titileo.
3. **US‑4.6** tiene el "Para" de otra historia ("tener una penalización si esta entidad entra…") y el "Quiero" de espectar.
4. **US‑10.1** conserva "agregar el modo multijugador en el Hito 3 sin refactoring mayor", que ya no aplica.
5. **US‑1.6 / 7.2** vs. **minuta 2**: el control Bluetooth "se mantuvo como requisito" (16/05) pero 1.3 y US‑1.2 lo hacen opcional. Conviene registrar el cambio de decisión.
6. **1.2 / Límites** dicen "tres modalidades" como alcance, pero 7.2 y R‑01 las dan como no aceptadas; está bien como estado, pero el doc no dice que la mirada ya cubre apagar la linterna (ver A.1).
7. **5.1**: la estimación por épica no incluye la épica 12 (Reliquias) ni las US nuevas (1.6, 1.7, 2.5‑2.7, 3.8, 3.9, 8.7, 8.8, 9.8, 9.9, 10.3, 10.4).
8. **Numeración**: hay dos encabezados "12. Reliquias del ritual"; los casos IT saltan de IT‑005 a IT‑007; CP‑057/058 trazan a "US‑12.1/13" (no existe US‑13).
9. **2.2.9** sigue presupuestando "hosting del servidor multijugador (free tier)", pero la arquitectura es LAN sin servidor.
10. **2.2.6** dice que los assets 3D salen de Sketchfab/Asset Store; los modelos actuales son de Meshy AI.
11. **2.5** (análisis económico‑financiero) contiene la lista de procedencia de audio, que temáticamente pertenece a US‑10.4 / recursos clave.
12. **9.1.2** pide Android 14+ e iOS 17+ para la campaña, mientras el build tiene deployment target iOS 26.0 (ningún iPhone con 17‑25 puede instalarlo).

---

## Parte D — ¿El doc nuevo cubre los "Pendientes de actualizar" de `CLAUDE.md`?

| Pendiente en `CLAUDE.md` | ¿Lo cubre el doc nuevo? |
|---|---|
| Licencias de audio y crédito obligatorio a Little Robot Sound Factory | **Parcial**: lista fuentes (2.5) y exige créditos (US‑10.4), pero omite a Mike Koenig y no hay créditos en la app. |
| Estado de las US de audio (4.1, 4.4, 6.4, 7.x, 11.3) | **Sí** (ahora "implementación observada/parcial"). Pero US‑4.4 en realidad no está implementada (A.4). |
| Calibración manual sin imagen (reescribir US‑2.1 / US‑2.2) | **Sí** (US‑2.2 y US‑2.5 nuevas, retrospectiva 2). |
| Reliquias + récord en el menú | **Sí** (épica 12, US‑12.1, CP‑105/106). |
| Modo espectador | **Sí** (US‑4.6, US‑9.9, CP‑107). |
| TUTORIAL como camino de pasos | **No.** |
| Sin bloqueo por versión de SO; aviso por falta de ARCore | **No, y lo contradice**: US‑1.4 / 10.1 vuelven a exigir un gate de SO (A.1). |
| Tutorial del primer escaneo | **No.** |

---

## Nota aparte: `CLAUDE.md` también está desactualizado en dos puntos

- Dice que `ArbmosDebug` sobreescribe un "disparador de quietud" (esfera/ventana/gracia) con `ArbmosQuietudViz`. Hoy `ArbmosDebug` sólo ajusta ocultación/exposición/demora del golpe y **Arbmos no usa quietud** (coincide con el doc nuevo, US‑6.1).
- Dice que los marcadores **no** se reconstruyen en cargas `DisplayOnly`; hoy sí se reconstruyen (invisibles) como puntos de spawn del Sorken (coincide con el doc nuevo, 8.4).
