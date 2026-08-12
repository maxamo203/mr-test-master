# Mortuorium — contexto de proyecto (destilado de la documentación)

> Resumen de trabajo para agentes/devs, generado a partir de `docs/Grupo112_Documentacion_v9.0.pdf`
> (82 páginas, documento de cátedra UNLaM, generado 2026-08-10). Este archivo evita tener que
> reabrir el PDF para preguntas frecuentes; para el detalle exacto (tablas completas, diagramas,
> criterios de aceptación palabra por palabra) o ante cualquier ambigüedad, volver al PDF —
> es la fuente completa.
>
> **Nota sobre fuentes de verdad duplicadas:** `CLAUDE.md` declara `Assets/Documentacion
> Mortuorium v9.0.md` como fuente de verdad, pero ese archivo es en realidad un volcado crudo
> del docx **v6.0** (solo renombrado a "v9.0" en el commit `9763edf`, sin actualizar contenido).
> El documento realmente vigente y más reciente es `docs/Grupo112_Documentacion_v9.0.pdf`, del
> cual sale este resumen. Vale la pena señalarle esto al equipo antes de confiar ciegamente en
> el `.md` de `Assets/`.

## 1. Qué es el proyecto

**Mortuorium** — juego de terror en realidad mixta (MR) para celular + visor tipo cardboard,
Proyecto Final de cátedra UNLaM, equipo 112 (Bosch, Di Tommaso, Rios, Vallejos). Año 2026.

- **Product Owner:** Cristian Marcelo Rios. **Scrum Master:** Franco Nicolás Vallejos.
  **Desarrolladores:** Máximo Augusto Bosch, Giuliano Di Tommaso.
- **Objetivo:** sesiones de 5–10 min por noche, gestionando linterna/batería y cordura,
  protegiendo el "libro del ritual", a lo largo de **6 noches** de dificultad creciente, con
  tres entidades de comportamiento distinto (Sorken, Arbmos, Veleth).
- El **entorno físico real del jugador** se escanea una vez (mapeo espacial del dispositivo) y
  se reutiliza entre sesiones; todo el contenido se ancla a una **imagen de referencia** fija.
- **Joystick externo Bluetooth**: botón principal enciende/apaga la linterna (mecánica central),
  botón secundario confirma acciones / emite un ping visible-audible para coordinación en
  multijugador sin depender del chat de voz.
- **Multijugador**: 1 a 4 jugadores, LAN local (no requiere internet para jugar), dificultad
  escala automáticamente según cantidad de jugadores.

### Límites explícitos del alcance actual

**Incluido:** 6 noches (sin niveles adicionales), solo las 3 entidades (Sorken/Arbmos/Veleth,
sin agregar más), una única locación (la casa), sin integraciones de logros/leaderboards/tiendas,
sin plugins de audio/efectos de terceros.

**No incluido:** localización a otros idiomas, soporte para visores VR de gama alta (Quest,
Pico), soporte para pisos desnivelados/con escalones (se asume piso plano horizontal).

## 2. Mecánicas core

### Noches y progresión
- 6 noches jugables, cada una más difícil (mayor frecuencia de entidades, menor duración de
  batería, mayor consumo de cordura, mayor frecuencia de oscuridad en el libro).
- Progreso guardado entre noches (en multijugador, lo guarda el host).
- Duración objetivo por noche: 5–10 min.

### Escaneo del entorno
- Obligatorio antes de la primera noche; a partir del mapa, el sistema determina dinámicamente
  los puntos de spawn de baterías.
- Todo el modelado tiene como referencia de origen la imagen escaneada principal.
- Re-escanear la imagen principal (fines de recalibración) debe ser **opcional** durante el
  juego, no solo al inicio.

### Linterna y batería
- Batería limitada que se agota con el uso continuo; tras el 20% empieza a titilar y la
  intensidad baja progresivamente hasta agotarse.
- Baterías de repuesto dispersas por el entorno; no reaparecen en el mismo punto dentro de 1
  minuto, y el timer de reaparición solo arranca cuando el jugador se aleja del punto.
- Se recogen apuntando + botón de interacción del joystick, a corta distancia sin obstáculos.

### Cordura
- Regula qué acciones tiene disponibles el jugador; su pérdida es permanente dentro de una
  noche (se resetea al 100% al empezar la siguiente).
- Baja si la linterna está apagada más de 3s seguidos, y al ser "visto" por Arbmos si el
  jugador se mueve.
- A cordura 0 la interfaz se distorsiona (aviso de estado crítico) pero no mata de inmediato;
  la siguiente acción que invocaría a Arbmos sí se vuelve letal.

### Libro del ritual (Veleth)
- Objeto anclado sobre la imagen principal (o un QR/superficie) que hay que defender.
- Cada 30–50s aparece una "oscuridad" que intenta consumirlo; iluminarla 4s seguidos la disipa.
- Si no se defiende en 10s, el libro se pierde (irrecuperable) y se invoca a Veleth, que
  persigue activamente hasta atrapar al jugador.

### Entidades
| Entidad | Rol | Comportamiento clave |
|---|---|---|
| **Sorken** | Principal | Intenta entrar por puntos específicos (ventanas/armarios/ductos), cada uno con ruido propio. Iluminar 3s+ lo ahuyenta; tardar >5s (a <3m) lo deja entrar. Dentro, ilumina con 70% batería (sin cortes) para que se retire, ralentizándolo mientras persigue (~0.5 m/s). Contacto a <0.5m = muerte. |
| **Arbmos** | Secundaria (cordura) | Solo desde noche 4. Alucinación individual aleatoria; drena cordura si el jugador se mueve; linterna apagada aumenta su probabilidad. Letal solo si cordura=0 y se dispara otra acción que lo invoque. Distorsión de lente **localizada** en su zona de pantalla; jumpscare final tras escalar susurros/gritos. |
| **Veleth** | Oscuridad del libro | Se invoca solo al perder el libro del ritual; persigue activamente hasta atrapar. Comportamiento/apariencia a definir durante desarrollo. |

### Efectos visuales/sonoros
- Filtro VHS/cámara antigua permanente + aberración cromática/distorsión de lente en momentos
  de tensión (ver `TensionSystem`/`TensionDistortionOverlay`, ya implementado — US-11.2).
- Banda sonora ambiental que se adapta al nivel de peligro/evento activo (US-11.3, pendiente).

### Multijugador y escalado
- 1 a 4 jugadores, cada uno con su propio smartphone+visor+joystick, comparten sesión de
  noche. Escalado automático por cantidad de jugadores (ver tabla del PDF §1.2): puntos de
  entrada de Sorken, frecuencia de oscuridad del libro (+20/+40/+60%), consumo de cordura
  (+10/+20%).
- Chat de voz opcional; ping visual/sonoro (linterna + botón secundario) como respaldo
  independiente de la calidad de audio.

## 3. Riesgos identificados (con mitigación, PDF §1.5)

- **Latencia de red multijugador**: mitigar con dead reckoning (interpolación local de
  posiciones) y umbral de latencia máxima ajustable.
- **Latencia del joystick Bluetooth bajo carga de red simultánea**: probar desde etapas
  tempranas, ajustar períodos de reacción esperados.
- **Drift/re-localización según dispositivo**: mucho menor en iOS que en Android. Mitigación:
  sugerir iOS como plataforma óptima (Android sigue soportado, no óptimo) — **esto ya se
  implementó** como aviso único en Android (`NightMenuUI` US-1.5, commit `2373998`). También
  se pide reapuntar periódicamente a un punto de referencia (QR/imagen) para resincronizar.
- **Fatiga visual / motion sickness**: mantener 60fps, opciones de confort (reducir campo
  visual, punto de referencia fijo en HUD), sesiones de prueba tempranas con usuarios reales.
- **Sobrecalentamiento**: sesiones de 5–10 min acotadas a propósito; advertir si el dispositivo
  supera un umbral de temperatura.
- **Coordinación de voz no controlable**: complementar con el sistema de ping visual/sonoro
  (linterna + botón secundario del joystick), independiente de la calidad del audio.

## 4. Arquitectura (modelo C4, PDF §8.1)

- **Contexto**: un "Anfitrión/Creador" escanea el espacio, crea el mapa e inicia sesión; un
  "Jugador invitado" se une y juega sobre el mismo mapa físico. Ambos interactúan con la
  "Experiencia de Terror MR", que usa tracking/sensores de la plataforma XR del dispositivo
  (ARCore/ARKit).
- **Contenedores**: cada dispositivo corre la **misma aplicación** — el anfitrión activa un
  servidor autoritativo local (no hay backend en la nube); los clientes descubren al host por
  UDP y sincronizan por TCP. Cada uno guarda/importa su copia del mapa (JSON+PNG).
  **Decisión de arquitectura explícita**: el host actúa como servidor autoritativo en LAN; la
  sesión no depende de servicios cloud.
- **Componentes**: Interfaz de escaneo/sesión → Orquestador de escaneo/sesión → dominio
  espacial y gameplay (Autoría del mapa, Modelo espacial y calibración, Motor de gameplay,
  Entidades de gameplay) → Persistencia del mapa + Runtime multijugador + Registro/replicación
  de entidades → Infraestructura (AR Foundation/ARCore/ARKit, archivos locales, red LAN a otra
  instancia móvil).
- **Persistencia**: `Application.persistentDataPath/scans/<nombre>.json` (geometría +
  metadatos) + `<nombre>.png` (imagen de referencia). Paquete `.mscn` para exportar/importar un
  mapa entre dispositivos. Todo se serializa relativo al ancla física (`WorldOrigin`), lo que
  permite reconstruir el escenario tras recalibrar.
- **Puertos de red** (coinciden con el código actual — `NetworkConfig.DefaultPort` = 7777,
  `LanDiscovery.DiscoveryPort` = 47777): UDP 47777 para descubrimiento, TCP 7777 para el juego.
  Alcance: misma red Wi-Fi/LAN; no requiere internet para jugar ni recuperar un escaneo.

## 5. Stack tecnológico (PDF §8.2/8.3)

| Paquete | Versión | Uso |
|---|---|---|
| Unity Editor/Runtime | 6000.4.4f1 | Motor principal (coincide con `CLAUDE.md`) |
| AR Foundation | 6.5.0 | Capa AR multiplataforma |
| Google ARCore XR Plugin | 6.5.0 | Proveedor AR Android |
| Apple ARKit XR Plugin | 6.4.3 | Proveedor AR iOS |
| Unity Input System | 1.19.0 | Entrada táctil/gamepad/joystick BT/XR |
| Unity UI (uGUI) | 2.0.0 | Menús/paneles (aunque el proyecto usa mayormente IMGUI vía `MortuoriumTheme`) |
| XR Core Utilities | 2.5.3 | XROrigin y utilidades de tracking |
| XR Plugin Management | 4.5.2 | Selección/inicialización del proveedor XR por plataforma |

- Render pipeline: **Built-in** (no URP). Android → IL2CPP/ARM64; iOS → export a Xcode.
- Comunicación: descubrimiento UDP en LAN + transporte TCP propio (framing custom), sesiones
  de 1 a 4 jugadores.
- Lenguajes: C# (app/lógica de red), Objective-C++ (puente nativo iOS para `.mscn`), XML/Gradle
  (manifest/integración Android).
- Hardware: cámara + sensores de movimiento del smartphone; visor cardboard + joystick BT; en
  iOS compatible puede aprovecharse LiDAR.

### Discrepancias conocidas entre documentación y `ProjectSettings` actuales

- **Android**: `AndroidMinSdkVersion` en el repo está en **29** (Android 10), mientras que el
  criterio de aceptación de US-1.1 exige rechazar en runtime cualquier versión **por debajo de
  Android 14**. Esto es intencional: el build acepta instalarse desde API 29 para no cerrar
  mercado, pero el **chequeo de versión en runtime** (US-1.1, la próxima tarea) es lo que debe
  bloquear con un popup a los dispositivos entre API 29 y 33 (Android 10–13).
- **iOS**: `iOSTargetOSVersionString` está en **26.0**, más alto que el criterio documentado
  ("iOS 17 o superior"). El propio manual de instalación (PDF §10.5) ya señala que esto **debe
  revisarse antes de liberar** para alinearlo — no es algo que resolver ahora, pero conviene no
  asumir que 26.0 es el valor "correcto" si se toca ese campo.

## 6. Convenciones de código (PDF §8.5 — coincide con lo ya documentado en `CLAUDE.md`)

Estándar Microsoft C# adaptado a Unity: UTF-8, 4 espacios (no tabs), llaves estilo Allman, una
clase principal por archivo. PascalCase para tipos/miembros públicos (`I` para interfaces),
`_camelCase` para campos privados, `camelCase` para parámetros/locales. Preferir
`[SerializeField] private` sobre campos públicos mutables. Namespaces por dominio funcional.
Comentarios breves en español, solo para decisiones/invariantes/limitaciones no obvias — nunca
repetir lo que ya dice el código (esto ya está reflejado en las instrucciones globales del
proyecto). Reglas específicas ya cubiertas por `CLAUDE.md` (Awake/singletons/Input
System/`WorldOrigin`/`RaycastResolver`/`#if UNITY_EDITOR`/evitar `CreatePrimitive` en runtime).

## 7. Definición de completado (Done, PDF §3.4)

Una historia se considera DONE solo si: se ejecutaron y cumplen **todos** sus criterios de
aceptación; la implementación respeta la arquitectura cliente-servidor y las convenciones;
código y documentación fueron revisados por otro integrante; está integrado sin conflictos y
compila; las pruebas (unitarias/integración/automatizadas) pasan; se probó en al menos un
dispositivo real compatible; no hay fallas bloqueantes/críticas/altas abiertas; la
UI/feedback es comprensible y respeta las opciones de confort; la sesión se mantiene estable
5–10 min sin cierres ni desincronizaciones; la documentación está actualizada; y fue demostrada
y aprobada por el Product Owner.

## 8. Backlog por épica (PDF §3.2) — resumen

> **Importante**: la columna "Status" del PDF dice "Por hacer" en absolutamente todas las
> historias — es una plantilla académica nunca actualizada, **no reflejar el estado real del
> repo**. Para saber qué está implementado, mirar el código/`git log`, no esta tabla. Se marca
> ✅ donde ya se verificó implementación en el repo al momento de escribir esto (2026-08-10).

1. **Plataforma y Hardware** (US-1.1 a 1.5) — soporte Android/iOS+cardboard, joystick BT
   (conexión/reconexión), linterna sigue la orientación de cabeza, detección de compatibilidad
   ARCore, aviso de versión óptima.
   - US-1.5 ✅ implementado (aviso iOS, commit `2373998`).
   - **US-1.1 (chequeo de versión mínima Android 14/iOS 17 con popup bloqueante) — pendiente,
     es la próxima tarea (ver §10).**
2. **Escaneo de espacio físico** (US-2.1 a 2.4) — captura de imagen/QR de referencia, escaneo
   obligatorio antes de jugar, recalibración opcional, persistencia entre noches. ✅ Núcleo del
   scanner (`Assets/Scanner/`) ya implementado según `CLAUDE.md`.
3. **Sistema de linterna y batería** (US-3.1 a 3.7) — consumo continuo, HUD de batería, aviso
   de batería baja, spawn de baterías con reglas de reaparición, recolección por joystick. ✅
   Ya implementado (Bateries/`BatterySpawnManager`, HUD).
4. **Entidad Sorken** (US-4.1 a 4.6) — ruidos por punto de entrada, ahuyentar iluminando,
   ingreso si se demora, persecución, muerte. ✅ Ya implementado.
5. **Sistema de cordura** (US-5.1 a 5.4) — barra de cordura, drenaje por linterna apagada,
   distorsión a cordura 0, no recuperable en la noche. ✅ Ya implementado (`LocalSanity`,
   `SanityHUD`).
6. **Entidad Arbmos** (US-6.1 a 6.4) — invocación por inactividad, letalidad condicionada,
   distorsión localizada, escalado de audio. ✅ Ya implementado (`Assets/Entities/Arbmos/`).
7. **Entidad Veleth y libro del ritual** (US-7.1 a 7.6) — anclaje del libro, ataques de
   oscuridad periódicos, defensa iluminando, pérdida y convocatoria de Veleth, persecución.
   Estado real a verificar en código (no confirmado en esta pasada).
8. **Noches, dificultad y progresión** (US-8.1 a 8.6) — 6 noches, aviso de entidades presentes,
   reloj en pantalla, guardado de progreso, escalado por noche, duración 5–10 min.
9. **Multijugador y escalado** (US-9.1 a 9.7) — sesiones 1–4, salas LAN, escalado automático,
   chat de voz, ping. ✅ Networking base y voice chat ya implementados (`Assets/Network/`,
   `Assets/Voice/`, commit reciente `93ca9af add voice chat`).
10. **Infraestructura técnica** (US-10.1, 10.2) — arquitectura cliente-servidor sin refactor
    mayor en Hito 3, opciones de confort (brillo/aberración/sonido/reducción motion sickness).
11. **Efectos visuales y sonoros** (US-11.1 a 11.3) — filtro VHS, aberración cromática/distorsión
    en tensión, banda sonora adaptativa.
    - US-11.1 (filtro VHS permanente) — verificar estado.
    - US-11.2 ✅ implementado (`TensionSystem`/`TensionDistortionOverlay`, commit `98c51c4`).
    - US-11.3 (banda sonora adaptativa) — pendiente, no vista en el repo.

## 9. Roadmap (PDF §11 — "hoja de ruta 2026")

| Release | Ventana | Épicas | Resultado objetivo |
|---|---|---|---|
| R1 Escaneo de entorno | 11 jun – 25 jun | 1, 2 | Base espacial capturada y reutilizable |
| R2 Prototipo Sorken | 25 jun – 23 jul | 3, 4 | Primer bucle jugable de amenaza y defensa |
| R3 Prototipo Arbmos | 23 jul – 20 ago | 5, 6 | Presión de cordura + segunda amenaza |
| R4 Prototipo Veleth | 20 ago – 3 sep | 7 | Objetivo de defensa del ritual y tercera amenaza |
| R5 Beta single player | 3 sep – 17 sep | 8 | Noches/dificultad/progresión listas para beta |
| R6 Alpha | 20 ago – 1 oct (paralelo) | 9, 10, 11 | Alpha técnica: red, escalado, pulido audiovisual |

**Hoy (2026-08-10)** el proyecto está formalmente en ventana R3/R4 según fechas, pero por
código ya hay trabajo de R6 en curso (multijugador, voice chat, tension system) — el roadmap
del PDF no está siendo seguido estrictamente en orden, cosa a tener presente pero no a "corregir".

## 10. Próxima tarea: chequeo de versión (US-1.1)

Ver el criterio de aceptación exacto en PDF pág. 27 (Historia de Usuario US-1.1, criterio 1):

> Dado que el usuario acaba de descargar e instalar el juego, cuando inicie el juego por
> primera vez, entonces el juego debe verificar que la versión del dispositivo **no** sea
> inferior a: **Android 14**, **iOS 17**. Cualquier otro sistema operativo o versión inferior
> no es aceptado y debe mostrarse un popup: *"Lo sentimos, la versión de su dispositivo no es
> compatible con el juego."*

(El "criterio 2" impreso debajo de este en el PDF, sobre resincronizar el mapa cada 30s, es un
error de copiado del documento — pertenece a US-2.1, no a US-1.1. El propio plan de pruebas
(CP-002) lo señala como inconsistencia.)

Caso de prueba asociado: **CP-001** (US-1.1/CA-1) — instalar/abrir por primera vez en
Android 14+, iOS 17+ y versiones inferiores; debe aceptar plataformas compatibles y bloquear
las inferiores con el mensaje definido, capturando SO/modelo/build como evidencia.

No hay código de chequeo de versión existente en el repo (`NightMenuUI`, `GameOptions`, etc.
no tienen nada relacionado — se verificó por búsqueda). Es una feature nueva a implementar
desde cero. El plan de implementación se discute por separado.
