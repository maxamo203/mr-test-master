# CLAUDE_MEMORY.md — Memoria del proyecto (aprendizajes de sesiones)

> Volcado consolidado de la memoria persistente de Claude Code para este proyecto,
> pensado para que **otro Claude** (u otra sesión/máquina) lo lea y lo use.
>
> **Cómo usarlo:** son observaciones puntuales (con su fecha), NO estado vivo. Antes de
> afirmar algo como hecho, **verificá contra el código actual** — archivos/líneas/flags
> pueden haber cambiado. Cada entrada tiene un tipo: `project` (cómo funciona algo no obvio),
> `feedback` (cómo quiere el usuario que trabaje). Los `[[nombre]]` enlazan entradas.
>
> Generado: 2026-07-17. El proyecto es un juego de terror MR en Unity 6000.4.4f1 +
> AR Foundation 6.5 (ARCore Android / ARKit iOS). Comentarios/identificadores en español.

---

## Índice

1. [Gameplay siempre multijugador](#1-gameplay-siempre-multijugador) — `feedback`
2. [Multijugador: origen = imagen de referencia compartida](#2-multijugador-origen--imagen-de-referencia-compartida) — `project`
3. [Cardboard: estéreo, passthrough, aspecto y oscuridad](#3-cardboard-estéreo-passthrough-aspecto-y-oscuridad) — `project`
4. [Cardboard: crash al salir por 2 ARCameraBackground](#4-cardboard-crash-al-salir-por-2-arcamerabackground) — `project`
5. [IMGUI: tap vs área segura (notch)](#5-imgui-tap-vs-área-segura-notch) — `project`
6. [Shader stripping: materiales runtime salen magenta](#6-shader-stripping-materiales-runtime-salen-magenta) — `project`
7. [LFS: assets faltantes en la Mac (Sorken invisible)](#7-lfs-assets-faltantes-en-la-mac-sorken-invisible) — `project`
8. [iOS: linking de Swift al agregar ARKit](#8-ios-linking-de-swift-al-agregar-arkit) — `project`
9. [iOS: GPU skinning (pista falsa del Sorken invisible)](#9-ios-gpu-skinning-pista-falsa) — `project`

---

## 1. Gameplay siempre multijugador
**Tipo:** `feedback` · (~2026-07)

Toda la lógica de gameplay (terror: Sorken, cordura, entradas, baterías) debe pensarse
**siempre para multijugador**, server-authoritative sobre el netcode existente
(`NetworkManager`/`EntityRegistry`/`NetworkEntity`/`ServerSpawn`, patrón `*Network`). Un
jugador solo = el mismo peer es **host y cliente a la vez**; NO existe rama de código "para
un jugador" vs "para varios" — siempre modelar como varios.

**Why:** el usuario lo pidió explícito ("no hay o no debería haber lógica para cuando es un
jugador o varios, siempre tomalo para varios"). Un spawner/lógica single-player propuesto
fue rechazado.

**How to apply:** entidades nuevas siguen el patrón `Sorker`/`SorkerAI`(server-only)/
`SorkerNetwork`. Estado por-jugador (cordura, linterna on/off + aim) va networked en
`PlayerEntity`/`PlayerNetwork`. El gameplay corre en **SampleScene** cargando un escaneo
guardado como mapa compartido; ScannerScene es solo autoría. Los marcadores se cargan en
`DisplayOnly` pero **invisibles** (son spawn points del Sorken). Relacionado: [[2]].

---

## 2. Multijugador: origen = imagen de referencia compartida
**Tipo:** `project` · (2026-06-14, ~33 días al generar esto — verificar)

El multijugador se construye en **SampleScene**. El origen compartido entre dispositivos es
la **imagen de referencia del mapa escaneado**, no cloud anchors
(`CloudAnchorHost`/`CloudAnchorResolver` quedan sin uso).

**Flujo:** el host elige un mapa guardado (`.json`+`.png` de ScannerScene) → lo carga
display-only y registra su imagen de ref → al unirse un cliente se le envía el `.mscn` por
TCP (`MessageType.MapData` + `MapDataMsg`, reusando `ScanPackage.Pack/Import`) → el cliente
lo importa, reconstruye el mapa y escanea la **misma** imagen física → ambos `WorldOrigin`
calibran contra ella → los spawns anchor-relativos (Sorkens) caen en el mismo lugar físico.

- **Sin avatares de jugador por ahora:** `NetworkManager._spawnPlayers=false`; el host es
  servidor puro (sin cliente loopback); solo se spawnean/sincronizan Sorkens.
- `ScanLoader.LoadForDisplay` (flag `DisplayOnly`) reconstruye paredes/cubos sin las
  esferas-handle de edición (lo consultan `WallObject.Create`/`CubeObject.Create`).
- El toggle de Cardboard está en el panel "Running" de `GameBootstrapper`.

Relacionado: [[1]], [[3]], [[6]].

---

## 3. Cardboard: estéreo, passthrough, aspecto y oscuridad
**Tipo:** `project` · (2026-07-17, actual)

Modo Cardboard (`MRCardboardController`): ojo izq = cámara AR (viewport mitad izq, rect
`0,0,.5,1`), ojo der = cámara hija con `localPosition=(ipd,0,0)` (viewport mitad der, rect
`.5,0,.5,1`), ambas con su `ARCameraBackground` recortado por `ARCameraLateralCrop.shader`
(`_CropOffsetX`/`_CropScaleX`). Se bloquea landscape al entrar.

**"Objetos virtuales corridos entre ojos" NO es bug:** es la disparidad binocular del offset
IPD (efecto profundidad). Las dos cámaras comparten rotación/FOV/viewport-igual → solo
difieren en horizontal. Se nota mal porque el **passthrough es monoscópico** (una cámara
física recortada en dos): el mundo real no tiene disparidad y los virtuales sí. **Decisión:**
IPD default **0** (objetos alineados), expuesto como slider "Profundidad (IPD)" en el menú de
pausa Opciones→Cardboard. Lo maneja `CardboardCalibrationUI` (PlayerPrefs `cardboard_ipd`,
llama `SetIPD`).

**Salir de Cardboard — NO crashea, pero congelaba el passthrough + trababa landscape.**
Síntoma: la sesión sigue trackeando (los virtuales se mueven) pero la imagen se congela.
Corregido en `ExitCardboard`: (a) forzar rebuild del command buffer del `ARCameraBackground`
izquierdo (`enabled=false; enabled=true`) tras restaurar material default + viewport completo
— el blit del fondo se "pega" al salir del split/custom-material; (b) `RestoreOrientation()`
guarda/restaura los **4 flags** de autorotación (antes miraba solo `autorotateToLandscapeLeft`).

**REWRITE a RenderTexture (2026-07-18, implementado, falta probar en device).** Antes: 2
`ARCameraBackground` + recorte por shader `ARCameraLateralCrop` → crash al salir ([[4]]) y no
se podía corregir el aspecto sin desfasar los virtuales (el recorte tocaba solo el FEED, no la
proyección → "muevo poco y las paredes se mueven mucho"). **Regla aprendida:** cualquier
corrección que toque solo el feed rompe el alineado AR; feed y virtuales tienen que escalarse
juntos. **Arquitectura nueva:** UN solo `ARCameraBackground`. La cámara AR renderiza TODO
(passthrough + virtuales + oscuridad) a una `RenderTexture` `_fullRT` (el AR mono a textura).
Se muestra 2 veces con un Canvas Overlay + dos `RawImage`, cada una con su `uvRect`
(zoom + offset por ojo) y su tamaño con **letterbox** (aspecto nativo, barras negras). Como
passthrough y virtuales viven juntos en `_fullRT`, recortar/letterboxear nunca los desfasa.
Trade-off: MONO (sin estéreo real de profundidad; IPD ya era 0). Nunca 2 backgrounds → sin
crash. Salir: `targetTexture=null` + toggle `_bg.enabled` (rebuild, si no se congela) +
destruir Canvas/RT + `RestoreOrientation()` (4 flags). **Riesgo #1 a validar:** que el
`ARCameraBackground` renderice el passthrough DENTRO de la RT. El shader `ARCameraLateralCrop`
y sus materiales quedaron sin uso.

**`DarknessOverlay` debe tapar TODA la cámara.** El quad se dimensionaba con
`arCamera.fieldOfView`/`aspect`, pero AR Foundation reemplaza la proyección por las
**intrínsecas** (FOV real ≠ `fieldOfView`) → quedaba chico (márgenes) y en Cardboard cubría un
solo ojo. Fix: sobredimensionar el quad (`kOversize=2.5`, ancho `≥2.2·h`). El negro sobrante
se recorta contra el borde y el cono de la linterna es world-space por-ojo, así que pasarse
de tamaño no tiene costo.

**Menú de pausa (`PauseMenuController`):** reestructurado a Main (Opciones/Reanudar) →
Opciones (Control/Cardboard/Linterna). `CardboardCalibrationUI` pasó de panel IMGUI con botón
"Config" en pantalla a **servicio headless** (solo estado + props `Scale/OffsetL/OffsetR` +
`Save`, PlayerPrefs); el compositor de [[3]] los lee. Relacionado: [[4]], [[5]].

---

## 4. Cardboard: crash al salir por 2 ARCameraBackground
**Tipo:** `project` · (2026-06-23, ~23 días — verificar; ver nota de estado en [[3]])

`MRCardboardController` crasheaba al SALIR del modo (Android cerraba la app, iPhone quedaba
con cámara verde).

**Causa real (logcat):** el estéreo creaba un **segundo `ARCameraBackground`** en runtime
(ojo derecho). En AR Foundation 6.5 / ARCore, dos `ARCameraBackground` vivos adquieren la
misma imagen de cámara/environment-depth del subsystem; al desmontar el segundo (`Destroy` o
`SetActive(false)`), el siguiente `UnityARCore_session_update` libera una referencia nativa ya
muerta → `RefBase::decStrong` → SIGSEGV. El stack está dentro de `UnityARCore_session_update`,
**no** en el cambio de `Screen.orientation` (pista falsa).

**Why:** no se puede tener más de un `ARCameraBackground` simultáneo con este stack; el daño
lo causa la coexistencia, no el teardown.

**How to apply (solución robusta / rewrite pendiente):** para passthrough estéreo usar UN
solo `ARCameraBackground`. La cámara AR (único background) renderiza el passthrough a una
RenderTexture con `cullingMask = 0`; dos cámaras de ojo COMUNES (sin componentes AR, hijas de
la cámara AR para heredar la pose) hacen blit de esa RT a su mitad de pantalla con un
CommandBuffer en `CameraEvent.BeforeForwardOpaque` y dibujan los virtuales con offset ±IPD/2
encima. Crear/destruir cámaras sin AR es seguro. Trade-off: se pierde la oclusión por
environment-depth de los virtuales en cardboard. Este es también el fix correcto del aspecto
de [[3]].

> Estado (2026-07-17): el código ACTUAL sigue teniendo 2 `ARCameraBackground` vivos y ahora
> NO crashea (sí congelaba el passthrough al salir, ya corregido — ver [[3]]). Verificar si
> el crash reaparece antes de tocar esta zona.

---

## 5. IMGUI: tap vs área segura (notch)
**Tipo:** `project` · (2026-07-17, actual)

Los OnGUI escalados llaman `UIScale.Begin()`, que setea `GUI.matrix` con escala `Factor` **y**
una traslación al origen del área segura (`SafeArea.GuiRect` = `sg.x, sg.y`, excluye
notch/Dynamic Island/home indicator). El hit-test de tap (EnhancedTouch/Mouse, en `Update`)
tiene que invertir esa matriz COMPLETA: pasar a GUI top-left, restar `(sg.x, sg.y)` y recién
dividir por `Factor`.

**Why:** en Android `Screen.safeArea` = pantalla completa (`sg = 0`), así que dividir el tap
por `Factor` sin restar el área segura "funciona" y esconde el bug. En iPhone `sg.y`
(portrait) o `sg.x` (landscape) es ~59px: sin restarlo, la zona táctil queda corrida y un
botón pegado al borde superior (p. ej. el de pausa) cae DENTRO del notch → intocable.

**How to apply:** `var sg = UIScale.SafeGui; var pv = new Vector2((tapPx.x - sg.x)/f,
(Screen.height - tapPx.y - sg.y)/f);`. En la formulación ScaleRect (virtual→pixel), el mapeo
correcto es el mismo que `UIBlocker.AddVirtualRect`: `new Rect(sg.x + r.x*f, sg.y + r.y*f,
r.w*f, r.h*f)`.

Corregidos: `PauseMenuController.Update` y `RecalibrateButton` (ScaleRect). SIN el bug (no
tocar): `ReferenceCaptureUI`/`CardboardCalibrationUI` dibujan en px reales (sin `UIScale`);
`LiDARScanner`/`SelectionController` usan `UIBlocker.IsPointerOver` (ya aplica el offset).
**Regla:** solo tienen el bug los que dibujan con `UIScale.Begin()` Y hacen su propio
hit-test. Relacionado: [[1]].

---

## 6. Shader stripping: materiales runtime salen magenta
**Tipo:** `project` · (~2026-06, ~35 días — verificar)

El proyecto usa el **Built-in Render Pipeline** (shaders propios CGPROGRAM/UnityCG, ej.
`Custom/EdgeGrid`), NO URP — pese a que `CLAUDE.md` menciona URP.

**Síntoma:** materiales creados en runtime con `new Material(Shader.Find(...))` se ven bien en
el editor pero **magenta en el celular**. Causa: el shader no está incluido en el build y se
stripea (`Shader.Find` en runtime no garantiza inclusión).

**Regla:** para visuales runtime usar shaders garantizados en el build (los que están en
`ProjectSettings/GraphicsSettings.asset` → `m_AlwaysIncludedShaders`):
- `Custom/LitMarker` (`Assets/Shaders/LitMarker.shader`) — esferas/marcadores iluminados, una
  sola variante, agregado a Always Included por guid. Lo usan todas las bolas (piso, handles,
  calibración, puertas, previews).
- `Unlit/Color` — sólido plano (built-in, ya en Always Included).
- `Custom/EdgeGrid` — translúcido con grid (committeado, referenciado por .mat).

`Standard` y `URP/Lit` **NO** están en Always Included → salen magenta. Si hace falta otro
built-in lit, agregarlo a Always Included (cuidado: muchas variantes). Ref: `ARImageAnchor.MakeSphere`.

---

## 7. LFS: assets faltantes en la Mac (Sorken invisible)
**Tipo:** `project` · (2026-06-15, ~32 días — verificar)

**Causa real** del "Sorken invisible en iPhone pero OK en Android" (y de PNGs que no importan
en la Mac): los assets pesados están en **Git LFS** (`.gitattributes`: `*.png`, `*.fbx/*.FBX`,
`*.ttf`, etc.). Incluye la malla del Sorken `Meshy_..._Character_output.fbx`, sus texturas y
animaciones. Los objetos LFS se habían commiteado local pero **nunca se subieron al remote**
(el blob en git era un puntero de 133 bytes vs 22 MB reales).

**Consecuencia:** en Windows están hidratados (LFS instalado) → se ven, builds de Android OK.
En la Mac, sin el contenido LFS en el remote, el checkout deja **punteros de 133 bytes** con
nombre `.png`/`.fbx` → Unity no importa → el FBX del Sorken queda sin malla → el build de
iPhone (hecho en la Mac) sale **sin geometría = invisible**. Red/posición/mapa andan porque no
dependen de esos assets.

**How to apply:** desde la máquina con el contenido real (Windows) `git lfs push --all origin`
(hecho 2026-06-15, 239 MB). En la Mac: `git lfs install` + `git lfs pull` → hidrata todo,
Unity reimporta solo. Si reaparece con un asset nuevo: chequear que el commit incluya el push
de LFS al remote, no solo el puntero. Esto invalidó las pistas de [[9]] y [[6]].

---

## 8. iOS: linking de Swift al agregar ARKit
**Tipo:** `project` · (~2026-06, ~32 días — verificar)

Al exportar a Xcode, el link falla con `Undefined symbol:
__swift_FORCE_LOAD_$_swiftCompatibility51` (y `56`). Causa: se agregó el paquete
`com.unity.xr.arkit` (manifest.json). El plugin XR de ARKit fuerza la carga de las libs de
compatibilidad de Swift, pero Unity no agrega los library search paths del runtime de Swift
cuando el código de usuario no tiene Swift.

**How to apply:** NO quitar `arkit` (hace falta para AR en iPhone). El fix está automatizado en
`Assets/Editor/IOSBuildPostProcessor.cs` → `FixSwiftLinking`: agrega `LIBRARY_SEARCH_PATHS`
`$(TOOLCHAIN_DIR)/usr/lib/swift-5.0/$(PLATFORM_NAME)` y `.../swift/$(PLATFORM_NAME)` a los
targets `Unity-iPhone` y `UnityFramework`, y `ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES=YES` en el
principal (corre en el export). Para un Xcode ya exportado, aplicarlo a mano. Fallback si el
path swift-5.0 no resuelve: agregar un `Dummy.swift` vacío al target. Contexto: [[9]], [[6]].

---

## 9. iOS: GPU skinning (pista falsa)
**Tipo:** `project` · (2026-06-15, ~32 días — verificar)

> **NOTA: esta entrada fue una PISTA FALSA.** La causa real del Sorken invisible en iPhone
> eran los assets LFS faltantes en la Mac → ver [[7]]. El skinning GPU/CPU es indiferente al
> problema real. Se conserva por contexto histórico.

Contexto histórico: en iOS (Metal) los Sorken no se veían (invisibles, NO magenta) mientras el
mapa y la calibración andaban; en Android OK. Se creyó que era el skinning: el commit
`9486d2f "fix iphone"` cambió `gpuSkinning`/`meshDeformation` de `1`/`2` (GPU) a `0`/`0` (CPU),
y como el FBX del Sorken tiene `isReadable: 0`, con CPU skinning sobre malla no-readable el
mesh sale vacío en Metal. **Recomendación que quedó:** dejar `gpuSkinning: 1`,
`meshDeformation: 2` en `ProjectSettings.asset` (o activar Read/Write en el FBX si se quiere
CPU). Distinto de [[6]] (eso da magenta, no invisible).

---

_Fin del volcado. Si actualizás algo acá, actualizá también la memoria viva en
`~/.claude/projects/<proyecto>/memory/` (índice `MEMORY.md` + un archivo por entrada)._
