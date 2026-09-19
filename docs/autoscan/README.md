# Escaneo automático por profundidad (BETA)

Alternativa al escáner manual: el usuario camina el cuarto y las paredes se arman solas a
partir de la **API de profundidad de ARCore** (y del piso detectado). Vive en
`Assets/AutoScan/` (namespace `Mortuorium.RoomScanning`), se entra desde
**ESCANEAR ENTORNO → + ESCANEO AUTOMÁTICO (BETA)** y **no toca** el escáner manual
(`Assets/Scanner/`, `ScannerScene`) ni el `AutoWallBuilder` de planos verticales.

Portado desde el proyecto standalone `Mortuorium-test` (rama `wall-corner-editing-2`). Los
documentos de este directorio son los de ese proyecto; esta página lista lo que **cambia acá**.

## Flujo

1. Menú → ESCANEAR ENTORNO → **+ ESCANEO AUTOMÁTICO (BETA)** → `AutoScanScene`
   (`SceneFlow.EscenaEscanerAuto`).
2. El origen se detecta solo (esquina o pared) → **REVISAR ORIGEN** (arrastrable) → CONFIRMAR.
3. **Elegir modo** de detección (HYBRID, DEPTH, PLANES, FLOOR, FLOOR+DEPTH) → CONFIRMAR.
   Cambiar de modo después **borra todo menos el origen**. Ver [wall-sources.md](wall-sources.md).
4. Mapeo: caminar el cuarto. En FLOOR / FLOOR+DEPTH se barre el piso y se confirma una vez.
5. **GUARDAR Y EDITAR** → guarda con `ScanSerializer` (mismo formato y misma lista que los
   demás escaneos, con `hasFloor`) y abre `ScannerScene` en modo edición
   (`ScannerLaunchParams.EditScanName`): ahí se captura la **imagen de referencia**, se alinea
   con AJUSTAR ORIGEN y se afina con las herramientas del juego.

## Qué es distinto respecto del proyecto standalone

- **Render pipeline: Built-in.** Los shaders `Mortuorium/ARPlaneGrid` y
  `Mortuorium/OriginGizmo` se reescribieron en CG (`Assets/AutoScan/Resources/Shaders/`, dentro
  de `Resources` para que `Shader.Find` no los pierda en el build). Las paredes usan el
  `Custom/EdgeGrid` y `WallEdgeGrid.mat` **del juego**. Los docs que hablan de URP/`_BaseColor`
  describen el proyecto de origen.
- **Un solo assembly.** Vive en `Assembly-CSharp` (necesita `ScanSerializer`, `SceneFlow`…);
  se aísla por carpeta y namespace. Por el mismo motivo `WallObject`/`WallMeshBuilder` propios
  se renombraron `AutoWallObject`/`AutoWallMeshBuilder` (el juego tiene los suyos en `Scanner`).
- **`ScanData` real.** No hay espejo: `ScanDataExporter` usa `Scanner.ScanData`/`ScanSerializer`.
- **`ARQuality`.** Fuerza la profundidad por nivel de calidad en cada carga de escena;
  `ARSessionSetup.Start` la vuelve a poner en `Best` (el mapeo no funciona sin ella).
- **Herramientas de desarrollo** (panel TUNE, EXPORT MAP, logs por reconstrucción) sólo en
  `Debug.isDebugBuild`, según las reglas de performance del juego.
- El origen es una esquina detectada, **no** la imagen de referencia: hasta capturarla en
  `ScannerScene` el cuarto queda relativo a esa esquina (calibración imagen-primero = v2).

## Estado

- Código portado y compilado contra los assemblies del juego (0 errores).
- **Pendiente en el Editor** (no se puede desde afuera): crear `Assets/Scenes/AutoScanScene.unity`
  (base: `Assets/Prefabs/XR Origin Escaneo.prefab` + los componentes de `Assets/AutoScan`),
  agregarla a Build Settings y probar en dispositivo. Ver `docs/autoscan/wall-sources.md`
  y `tuning-menu.md` para los knobs.
- En dev build, `ARSessionSetup` dibuja abajo un panel de diagnóstico de profundidad (modo pedido/actual,
  soporte, textura, modo de fondo y keyword `ARCORE_ENVIRONMENT_DEPTH_ENABLED`): sirve para ver si la
  oclusión está realmente activa en el dispositivo.
- iOS (ARKit) sin probar: sólo se ejerció ARCore.
- El HUD está en inglés y con su propio IMGUI; pasarlo a `MortuoriumTheme`/español es un paso
  posterior.

## Armado de `AutoScanScene` (Editor)

**Atajo:** menú **Mortuorium > AutoScan > Build AutoScanScene** (`Assets/AutoScan/Editor/`) hace todo lo de
abajo solo: escena, prefab de plano, valores del mapper y Build Settings. Es re-ejecutable.
Sin la escena en Build Settings el botón del menú sólo loguea un error y no navega.

Base: `Assets/Prefabs/XR Origin Escaneo.prefab` (cámara AR, fondo, `AROcclusionManager`) + un
`ARSession`. En el GameObject del XR Origin agregar: `ARSessionSetup`, `ARPlaneManager`
(Horizontal + Vertical; su plane prefab lleva `PlaneClassificationVisualizer`), `ARAnchorManager`,
`ARRaycastManager`, `PlaneCollector` (material con el shader `Mortuorium/ARPlaneGrid`),
`CornerDetector`, `DepthOccupancyMapper` (valores en [scene-values.md](scene-values.md)),
`RoomBuilder` (`wallMaterial` = `WallEdgeGrid.mat`), `DoorDetector`, `JsonExporter`,
`RoomRelocalizer` y `RoomScanningManager`. `ScanDataExporter` y `WallEditor` se agregan solos
en `Awake`; `DetectionTuningMenu` sólo en dev build. Después: Build Settings + probar
(ARCore, Android). Cargar la escena por `SceneFlow.GoTo(SceneFlow.EscenaEscanerAuto)`.

## Índice

[wall-sources](wall-sources.md) · [auto-reconstruction](auto-reconstruction.md) ·
[room-graph](room-graph.md) · [tuning-menu](tuning-menu.md) ·
[wall-corner-editing](wall-corner-editing.md) · [wall-geometry](wall-geometry.md) ·
[wall-materials](wall-materials.md) · [scene-values](scene-values.md)
