# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Mixed-reality horror game built in **Unity 6000.4.4f1** (Unity 6.4) with **AR Foundation 6.5** (ARCore 6.5 on Android, ARKit 6.4.3 on iOS). The core idea: the player scans their physical room once; the scan (walls, cubes, doors) is persisted and reused across sessions. Scanning is anchored to a **fixed physical reference image** the player captures with the camera — this image lets every session recalibrate the relative positions of scanned elements. Gameplay on top of the scan is still being designed.

Comments and identifiers are predominantly in Spanish; match that when editing existing files.

## Build & run

There is **no CLI build, lint, or test setup** — this is a GUI-driven Unity project. Iterate through the Unity Editor and on-device builds.

- **Editor play mode:** open `Assets/Scenes/ScannerScene.unity` (the current scene) and press Play. AR subsystems are stubbed in the editor — see `ARImageAnchor.EditorStub` / the `#if UNITY_EDITOR` branches, which fake an anchor ~1 s after start so flows can be exercised without a device.
- **Android:** File > Build Settings > Android, IL2CPP / ARM64, min API 29. `Build And Run` to a connected device, or `adb install -r <apk>`. Requires ARCore + camera permission.
- **iOS:** export the Xcode project; `Assets/Editor/IOSBuildPostProcessor.cs` runs automatically on export to inject local-network Info.plist keys (needed for the LAN discovery in `Assets/Network`). Keep its `DiscoveryPort` in sync with `LanDiscovery.DiscoveryPort`.
- `Assembly-CSharp.csproj` / `.sln` are Unity-generated and gitignored; don't hand-edit them.

Saved scans live at `Application.persistentDataPath/scans/<name>.json` (plus a sibling `<name>.png` reference image) — app-private storage on device.

## Scenes

- **`ScannerScene.unity`** — the active scene; all scanner work happens here.
- **`SampleScene.unity`** — older MR / multiplayer-lobby scene (Cardboard controller, AR lobby). The `Assets/Network`, `Assets/AR/ARLobby*`, `Assets/AR/CloudAnchor*`, and `Assets/Entities` code belongs to this multiplayer lineage and is **not** wired into the scanner flow. The `README.md` describes only this older sample and is stale.

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

## Conventions

- Singletons follow the `Instance` + `Awake` self-destruct-on-duplicate pattern; init order matters and is set with `[DefaultExecutionOrder]` (bootstrap -100, FSM -50, registry -40).
- Cross-platform guards: `#if UNITY_EDITOR` stubs AR, `#if UNITY_IOS` for the build post-processor. Avoid `GameObject.CreatePrimitive` for runtime visuals — Physics modules can be stripped under IL2CPP and the implicit collider throws (see `ARImageAnchor.MakeSphere`).
- `.asset` files referenced by code (e.g. `ReferenceImageLibrary.asset`, `PrefabRegistry.asset`, materials, `Assets/Scanner/Markers/*.asset`) are committed; the gitignored `Library/`, `Temp/`, `build/`, `Logs/`, `UserSettings/` are not.
- **Input System package only** (`Active Input Handling = Input System`): the legacy `UnityEngine.Input` API throws `InvalidOperationException` at runtime (spams every frame if in an `Update`). Read input through `UnityEngine.InputSystem` (`Touchscreen`/`Keyboard`/`EnhancedTouch`). Systems that read raw taps in `Update` (`SelectionController`, `LiDARScanner`, `RecalibrateButton`) must consult `Scanner.UIBlocker.IsPointerOver(pos)` before acting, so taps on IMGUI panels don't leak through to the AR view. (`PlayerNetwork` still uses legacy `Input` — multiplayer/`SampleScene`, not the scanner.)
