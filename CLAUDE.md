# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Mixed-reality horror game built in **Unity 6000.4.4f1** (Unity 6.4) with **AR Foundation 6.5** (ARCore 6.5 on Android, ARKit 6.4.3 on iOS). The core idea: the player scans their physical room once; the scan (walls, cubes, doors) is persisted and reused across sessions. Scanning is anchored to a **fixed physical reference image** the player captures with the camera — this image lets every session recalibrate the relative positions of scanned elements. Gameplay on top of the scan is still being designed.

Comments and identifiers are predominantly in Spanish; match that when editing existing files.

## Source of Truth

**`Assets/Documentacion Mortuorium v9.0.md`** is the single source of truth for all project decisions, architecture, requirements, and specifications. All code changes, features, and technical decisions must respect and comply with what is documented there.

**When a request conflicts with the documentation:** If asked to implement something that contradicts or deviates from what is specified in `Documentacion Mortuorium v9.0.md`, I will ask for clarification and confirmation before proceeding. This ensures alignment with the official project specification and prevents unintended design drift.

## Build & run

There is **no CLI build, lint, or test setup** — this is a GUI-driven Unity project. Iterate through the Unity Editor and on-device builds.

- **Editor play mode:** open `Assets/Scenes/ScannerScene.unity` (the current scene) and press Play. AR subsystems are stubbed in the editor — see `ARImageAnchor.EditorStub` / the `#if UNITY_EDITOR` branches, which fake an anchor ~1 s after start so flows can be exercised without a device. There's no camera feed either, so `EditorPanorama360` (`Assets/AR/`, whole file under `#if UNITY_EDITOR`) puts an equirectangular panorama (`Assets/Editor/Panorama360/hotel360.jpg` — under `Assets/Editor/`, so it never ships) as the skybox and forces `clearFlags = Skybox`, giving the full-screen effects (VHS / distortion `CameraFX.shader`) something real to work on. Toggle: menu **Mortuorium > Fondo 360 en Play** (EditorPrefs, per machine). Same tier, same folder: `EditorPlayerControls` moves the AR camera with WASD (Shift run, Space/E up, Ctrl/Q down) and looks around while you *drag* the mouse (never free-look), disabling the camera's `TrackedPoseDriver` while it's on — toggle **Mortuorium > Controles WASD en Play**. Both toggles live in `Assets/Editor/MortuoriumEditorPlayMenu.cs`.
- **Android:** File > Build Settings > Android, IL2CPP / ARM64, min API 29. `Build And Run` to a connected device, or `adb install -r <apk>`. Requires ARCore + camera permission.
- **iOS:** export the Xcode project; `Assets/Editor/IOSBuildPostProcessor.cs` runs automatically on export to inject local-network Info.plist keys (needed for the LAN discovery in `Assets/Network`). Keep its `DiscoveryPort` in sync with `LanDiscovery.DiscoveryPort`.
- `Assembly-CSharp.csproj` / `.sln` are Unity-generated and gitignored; don't hand-edit them.
- **Splash screen**: the MORTUORIUM wordmark (`Assets/Splash/mortuorium_splash.png`, Bebas + the menu's red/teal chromatic glitch) is wired into Player Settings by the editor menu **Mortuorium > Configurar splash** (`Assets/Editor/MortuoriumSplashSetup.cs`) — run it once; it sets the dark theme background and shows the logo above the mandatory "Made with Unity" watermark (Personal license can't remove the Unity logo, only add above it via `DrawMode.UnityLogoBelow`).

Saved scans live at `Application.persistentDataPath/scans/<name>.json` (plus a sibling `<name>.png` reference image) — app-private storage on device.

## Scenes

- **`NightMenuScene.unity`** — entry scene: the MORTUORIUM main menu (`NightMenuUI`). It resolves **everything before the AR camera**: game mode, night and environment. Buttons: UN JUGADOR, MULTIJUGADOR (→ create-room / join), ESCANEAR ENTORNO (saved-scan management → `ScannerScene`), OPCIONES. UN JUGADOR and CREAR SALA share the sub-flow *night → environment → CONFIRMAR*; if there's no playable environment (a scan **with a reference image**) it shows the "falta un entorno" alert → scan. The choice is written to `GameSession` (`Mode` ∈ SinglePlayer/MultiHost/MultiClient, `SelectedNight`, `SelectedMap`, `HostPort`) and then `SampleScene` loads.
- **`ScannerScene.unity`** — the scanner; all scanning work happens here. Entered from the main menu with `ScannerLaunchParams.EditScanName` set (null = new scan; a name = `ScannerSceneBootstrap` auto-loads it for editing).
- **`SampleScene.unity`** — MR / multiplayer scene (Cardboard controller, AR lobby, gameplay). `GameBootstrapper` is now a thin executor: it reads `GameSession.Mode` and either **auto-hosts** (SinglePlayer/MultiHost → `StartHost` with the chosen map, then the SALA / sync screen; deferred one frame so `ARLobbyManager` has subscribed) or shows **UNIRSE** (MultiClient → LAN auto-discovery + manual IP, then connect). All mode/night/map selection UI moved to `NightMenuUI`. The `README.md` describes only the older sample and is stale.

Scene navigation goes through `SceneFlow.GoTo` (`Assets/UI/SceneFlow.cs`), which destroys the cross-scene singletons (`NetworkManager`, `EntityRegistry`, `WorldOrigin`) so the target scene starts clean. `SceneNavUI` is an obsolete empty shell (its prefab is still referenced in two scenes).

**LAN multiplayer** (`Assets/Network/`): `NetworkConfig.DefaultPort` is the single source for the game port. The host auto-advertises via `LanDiscovery` (UDP) **only when multiplayer and on the default port** — custom ports (set in the menu's "Avanzado" section) don't advertise, so clients must type `ip:port`. The client's UNIRSE screen lists auto-discovered rooms and accepts a manual IP (`ip` alone → default port). `LanAddress.BestLanIPv4()`/`AllLanIPv4()` pick the real LAN IPv4 to share, scoring RFC1918 private ranges + gateway + WiFi/Ethernet and penalizing virtual/VPN adapters (fixes Radmin VPN's 26.x showing up on Windows).

## UI Mortuorium (`Assets/UI/`)

All menus/HUD are IMGUI styled by **`MortuoriumTheme`** (static): palette, fonts (Bebas Neue / Special Elite / IBM Plex Mono under `Assets/Resources/Fonts/`, loaded via `Resources.Load`) and widgets (`Boton`, `Celda`, `CampoTexto`, `Slider`, `Gradiente`, `BotonVolver`, `Barra`, `Candado`). It was ported from the HTML prototype in `Assets/Prototipo Navegacion/` — that folder is the design reference, not runtime code. Buttons integrate with `ImguiGamepadMenu` (focus is drawn by the theme via `ImguiGamepadMenu.NextHasFocus`, a tan border, not the old yellow tint). `GameOptions` holds persistent user options (master volume).

**Full-screen vs safe-area fills**: IMGUI draws through `UIScale.Begin`'s matrix, which maps virtual coords only to the *safe area* — a plain `Fill` leaves the notch/home-indicator strips showing whatever is behind (3D scene or raw camera). Opaque menu screens call `MortuoriumTheme.FillScreen(Bg)` (fills the whole physical screen, resetting the matrix); screens over the AR camera (scanner HUD, SINCRONIZACIÓN, sala) call `FillOutsideSafeArea(Bg)` so the camera shows only inside the safe area and the outside strips are solid black, continuing the UI's gradient to the screen edge.

**Pseudo-3D icons** (`MortuoriumIcons`): isometric tool glyphs (wall / cube / door / marker / floor / two recalibrate reticles) rasterized once into cached `Texture2D`s by code (no assets). Used in the scanner's bottom tool row (`ReticleController.DrawHerramientas`), which is a **horizontally drag-scrollable** strip (custom `Event.current` drag; a drag suppresses the button tap) with icon + full label per button. The two recalibrate actions (keep scene fixed / move scene with anchor) are entries in this strip — the old standalone `RecalibrateButton` is now an obsolete empty shell.

## Debug HUD (`Assets/DebugHud/`)

Every on-screen diagnostic lives under a single runtime-created `DebugHud` GameObject (DontDestroyOnLoad), one child GameObject + script per panel (`DebugRedUI`, `DebugEscanerUI`, `DebugRaycastUI`, `DebugFallbackUI`, `DebugDirectorUI`, `DebugBateriasUI`, `DebugLidarUI`, `DebugBenchmarkUI`). It is only created when `Debug.isDebugBuild` (editor / development build) — release builds have no debug UI at all — and can be toggled at runtime from the pause menu (Opciones → Debug HUD, dev-only; persisted in PlayerPrefs). Data sources expose read-only snapshots (`GameDirector.DebugSnapshot()`, `BatterySpawnManager.DebugSnapshot()`, `LiDARScanner.DebugSnapshot()`, `ARTrackingBenchmark.Report`, `ReticleController.LastHit`) instead of drawing their own OnGUI; `NetworkDebugUI` is an obsolete empty shell. Flashlight tuning sliders (rango/ángulos/intensidad) in the pause menu are dev-build-only too. Same tier: **Opciones → ARBMOS (DEV)** (`ArbmosDebug`) overrides the Arbmos stillness trigger (sphere radius / window / grace) and toggles the world-space wireframe of the stillness sphere (`ArbmosQuietudViz`, `Graphics.DrawMesh` line meshes, host only — the director is server-authoritative); with the override off, the `NightConfig` values rule, exactly as in release.

## Core architecture

**Everything is anchor-relative.** `WorldOrigin` (`Assets/AR/WorldOrigin.cs`) is a `DontDestroyOnLoad` singleton parented under the current AR anchor. All scanned/networked positions are stored as offsets relative to it (`ToRelative`/`ToWorld`). When the SLAM system corrects the anchor pose, the whole scanned scene follows automatically because it hangs off `WorldOrigin`. Serialized `ScanData` is likewise all anchor-local — loading reconstructs GameObjects parented to `WorldOrigin` from those local transforms.

### Scanner (`Assets/Scanner/`) — the heart of the app

- **`ScanStateMachine`** — singleton FSM. `ScannerMode` enum drives the whole UI: what the reticle's "Place" button does and which panels are visible. Starts in `Calibrating`. Subscribe to `OnModeChanged` / `OnSelectionChanged`. Most components key their behavior off `ScanStateMachine.Instance.Current`.
- **`ScannerSceneBootstrap`** (`DefaultExecutionOrder(-100)`) — scene entry point. Ensures the singletons exist, forces `Physics.autoSyncTransforms = true` at runtime (the project ships with it off), and on `ARImageAnchor.OnImageReacquired` moves the FSM `Calibrating → Idle`. Lists in its header comment exactly which sibling components must sit on `ScannerRoot`.
- **`SceneRegistry`** — live registry of `WallObject`/`CubeObject`/`MarkerObject` in the scene; `Capture(name)` snapshots them into `ScanData`, `ClearAll()` tears the scene down before a load. `FindWall(id)` resolves the parent wall when reconstructing markers.
- **`ScanData` / `ScanSerializer`** — `[Serializable]` DTOs (`JsonUtility`) and disk I/O. `ScanData.CurrentVersion` gates format migrations. `refImageWidthMeters > 0` means a reference PNG is stored alongside the json.
- **Builders** (`WallBuilder`, `DoorBuilder`, `CubeBuilder`, `MarkerBuilder`) — each owns a multi-step placement flow expressed as FSM modes (e.g. walls are a polyline: `Wall_V1 → Wall_Height → Wall_Vn …`; cubes/doors are two-corner diagonals). Builders expose `static` configured materials that the `*Object` classes read when (re)constructing. Doors are stored as `u`/`v` ranges along a parent wall, not free transforms.
- **Markers** (`MarkerBuilder`, `MarkerObject`, `MarkerType`/`MarkerCatalog`) — points-of-interest placed on a wall to flag "there's a door/window here" **without touching geometry**; visually a colored sphere + a normal arrow, picked from the "Identificar" submenu (not the general add list). Anchored **relative to a wall** (`wallId` + `u,v` + `side`), never a free transform: `WallObject.Rebuild()` re-syncs its markers so they follow wall edits, and `WallObject.Delete()` deletes them. `side` (±1 along the wall normal) records which face you were looking at — the two faces are **distinct** markers. Placement and move both project the reticle onto the wall via a Physics raycast against the `Placed` layer (`MarkerBuilder.TryResolveOnWall` / `TryResolveOnSpecificWall`), **not** the `RaycastResolver` AR cascade (whose hit isn't on the wall surface → offset). Marker types are **data-driven ScriptableObjects, not an enum**: one `MarkerType` asset (id / displayName / color) per type, gathered in a `MarkerCatalog` asset wired to `MarkerBuilder` (`MarkerCatalog.Active` publishes it for load-time id→type resolution); `MarkerData.kind` stores the type `id`, so adding a type is editor-only. Markers are **not** rebuilt on `ScanLoader` `DisplayOnly` loads (multiplayer map is visual-only).
- **`RaycastResolver`** — cross-platform raycast cascade returning the first valid hit: (1) Physics vs. the `LiDARMesh` layer (iOS LiDAR mesh colliders), (2) `ARRaycastManager` (planes / ARCore Depth / feature points), (3) a fallback point along the camera ray. Always go through this rather than calling AR raycasting directly.
- **Selection/editing** — `ISelectable`, `SelectionController`, `TransformGizmoController` + `*VertexHandle` / `GizmoHandle` give the move/edit gizmos. Handle spheres rely on `autoSyncTransforms` being on (see bootstrap).
- **`ReferenceCaptureUI`** — the `Calibrating`-mode flow: the user frames a rectangle, captures a camera fragment, sets its real-world width (cm), and that fragment is registered as the AR reference image and saved with the scan.

### AR anchoring (`Assets/AR/`)

`ARImageAnchor` wraps `ARTrackedImageManager` and is the calibration backbone. Reference images are added **at runtime** into a `MutableRuntimeReferenceImageLibrary` (`AddReferenceImage` → async validation job → `RestartTracking`), so the "fixed image" can be a fragment the player just photographed or one loaded from a saved scan. Key invariants:

- `RestartTracking` **detaches `WorldOrigin` from the old anchor before destroying it** — destroying the anchor while `WorldOrigin` is still a child would take the entire scanned scene with it.
- The anchor's Y axis is forced upright (`UprightFromImage`) regardless of the physical image orientation; only horizontal heading is kept.
- A `_reacquireDelay` window prevents the stale trackable from re-detecting in the same frame on recalibration.
- `WorldOrigin.SetOrigin(anchor, keepVisualPosition)` chooses recalibration semantics: `false` = scene moves with the anchor (preserve relative coords); `true` = scene stays put visually and only its anchor-relative coords change.

### Networking (`Assets/Network/`, `Assets/Entities/`) — older multiplayer path

Tick-based authoritative-server model over a hand-rolled TCP transport (`TcpTransportServer/Client`, `MessageFramer`, `MessageType`/`NetworkMessages`) with UDP `LanDiscovery`. `NetworkManager` is the singleton orchestrator (server spawns players/Sorkers, broadcasts a cloud-anchor id, gates `GameStarted`). Entities (`PlayerEntity`, `Sorker`/`SorkerAI`) split into a sim component + a `*Network` sync component. This is wired to `SampleScene`, not the scanner — treat it as a separate subsystem unless explicitly bridging the two.

## Performance & build tiers (IMPORTANT)

This is a stereoscopic AR game on mid-range phones — **runtime performance is a hard requirement** (sub-60fps causes motion sickness; see the risk section of the design doc). **Anything that exists to help development must NOT cost resources in the shipped (release/prod) build.** Before adding any diagnostic, tuning knob, logging, or convenience, decide which of these three tiers it belongs to and gate it accordingly — always pick the *strictest* tier that still serves the purpose:

- **Editor-only** — wrap in `#if UNITY_EDITOR` so it is compiled out of *every* build (dev and prod). Use for pure authoring/tuning aids that only make sense while iterating in the Unity Editor and have a per-frame cost: e.g. `ArbmosSmokeAura`'s live particle tuning (`PushParams` in `Update`), editor menu tools (`Assets/Editor/`), gizmos. In a build the values are applied once at spawn instead.
- **Development build** — gate on `Debug.isDebugBuild` (true in Editor + Development Build, false in release). Use for in-game dev options that a tester on-device may want to toggle: the whole `DebugHud` (only created when `Debug.isDebugBuild`), the pause-menu flashlight sliders and Debug-HUD toggle, the **dev NightConfigs** (`NightMenuUI._devNights` — release always uses `_nights`/prod), and per-night testing switches like `NightConfig.sorkenActive` / `GameDirector._practiceMode` (set only in the `Nights/dev` assets). Data sources expose read-only `DebugSnapshot()`-style methods so the release path never builds the strings.
- **Release / prod** — the default. No debug UI, no per-frame dev code, no dev content. Anything not gated by one of the above must earn its cost in the real game. Night assets live in `Assets/Gameplay/Nights/prod` (`_nights`) vs `Assets/Gameplay/Nights/dev` (`_devNights`); the menu picks the set with `Debug.isDebugBuild`, so dev nights never reach a release build.

Rule of thumb: a helper that only informs *you* while coding → `#if UNITY_EDITOR`; a helper a *tester* toggles on a real device → `Debug.isDebugBuild`; neither → it ships and must be justified on performance grounds. Never leave a per-frame `Debug.Log`, `OnGUI` diagnostic, or `FindObjectByType` scan running in release.

## Conventions

- Singletons follow the `Instance` + `Awake` self-destruct-on-duplicate pattern; init order matters and is set with `[DefaultExecutionOrder]` (bootstrap -100, FSM -50, registry -40).
- Cross-platform guards: `#if UNITY_EDITOR` stubs AR, `#if UNITY_IOS` for the build post-processor. Avoid `GameObject.CreatePrimitive` for runtime visuals — Physics modules can be stripped under IL2CPP and the implicit collider throws (see `ARImageAnchor.MakeSphere`).
- `.asset` files referenced by code (e.g. `ReferenceImageLibrary.asset`, `PrefabRegistry.asset`, materials, `Assets/Scanner/Markers/*.asset`) are committed; the gitignored `Library/`, `Temp/`, `build/`, `Logs/`, `UserSettings/` are not.
- **Input System package only** (`Active Input Handling = Input System`): the legacy `UnityEngine.Input` API throws `InvalidOperationException` at runtime (spams every frame if in an `Update`). Read input through `UnityEngine.InputSystem` (`Touchscreen`/`Keyboard`/`EnhancedTouch`). Systems that read raw taps in `Update` (`SelectionController`, `LiDARScanner`, `RecalibrateButton`) must consult `Scanner.UIBlocker.IsPointerOver(pos)` before acting, so taps on IMGUI panels don't leak through to the AR view. (`PlayerNetwork` still uses legacy `Input` — multiplayer/`SampleScene`, not the scanner.)
