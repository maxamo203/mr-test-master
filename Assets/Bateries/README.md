# Sistema de Baterías y Linterna

Ciclo de recurso para la linterna en el modo multijugador (SampleScene / `Assets/Network`):
la linterna se agota con el uso y el jugador busca **pilas de repuesto** repartidas por el
mapa. Hay **3 rarezas** (más rara → más carga, menos frecuente). Es **autoritativo del
servidor (host)**: el host decide dónde aparecen las pilas y las comparte con todos los
clientes en coordenadas anchor-relativas (idénticas en todos los dispositivos).

## Cómo funciona (resumen)

1. Al **arrancar la partida** (`NetworkManager.OnGameStarted`), el `BatterySpawnManager`
   —**solo en el host**— deriva los **puntos de spawn** del escaneo del mapa (arriba de los
   muebles y/o en el piso despejado).
2. Cada punto spawnea una pila de rareza elegida por peso (`ServerSpawn`), que se replica a
   todos los clientes.
3. El jugador **apunta** una pila con la cámara y aprieta un botón (pantalla / **A** del
   gamepad / tecla `E` en editor). El server valida cercanía, despawnea la pila y **acredita
   carga a la linterna** del que la recogió.
4. **Reaparición**: tras recoger, el punto reaparece a los `respawnSeconds`, pero el timer
   **solo corre cuando ningún jugador está cerca** del punto (si se queda, no reaparece).

## Archivos

| Archivo | Rol |
|---|---|
| `BatteryRarity.cs` | Clase `[Serializable]` de una rareza (data pura). |
| `BatteryRaritySet.cs` | **ScriptableObject** que agrupa las rarezas. Lo consume el manager. |
| `BatteryRaritySet.asset` | **El archivo de configuración** editable en el Inspector. |
| `BatteryEntity.cs` | La pila como `NetworkEntity` estática (se re-ancla a `WorldOrigin`). |
| `BatterySpawnManager.cs` | Gestor server-only: deriva puntos, spawnea, respawn, valida pickup. |
| `IContextAction.cs` | Interfaz de una acción del botón primario (A). |
| `ContextActionController.cs` | Hub del botón A: elige la acción por contexto (corre en todos). |
| `BatteryPickupAction.cs` | Acción: recoger la pila apuntada (prioridad alta). |
| `FlashlightToggleAction.cs` | Acción: prender/apagar la linterna (prioridad baja, fallback). |
| `BatteryMessages.cs` | Mensajes de red `BatteryPickup` / `BatteryCollected`. |
| `prefabs/` | Un prefab por rareza (con `BatteryEntity`). |

Cambios fuera de esta carpeta: `MessageType.cs`, `NetworkEntity.cs`
(`EntityTypeIds.BatteryBase = 10`), `NetworkManager.cs` (pose de jugadores + ruteo de
pickup), `NetworkMessages.cs` (`PlayerPoseMsg`), `Assets/Flashlight.cs` (consumo de carga +
`Toggle()`) y `Assets/FlashlightHUD.cs` (barra de carga, va en el GameObject de la linterna).

---

## La invariante clave (leer antes de tocar rarezas)

Cada rareza está atada por su **`rarityIndex` (0/1/2)** a través de tres lugares que **deben
coincidir**:

```
rarityIndex (en la rareza del RaritySet)
   ├─ = rarityIndex del componente BatteryEntity en el prefab
   └─ TypeId de red = 10 + rarityIndex  (BatteryBase)  →  registrado en PrefabRegistry
```

Ejemplo para la rareza común (index 0): `BatteryEntity.rarityIndex = 0`, y ese prefab va
registrado en `PrefabRegistry.asset` con **TypeId 10**. Index 1 → TypeId 11. Index 2 → 12.

Si algo no coincide, el `BatterySpawnManager` **valida al arrancar** y te dice el problema
exacto por Console (y no spawnea, para no romper).

---

## Cómo modificar la configuración de baterías

### 1. Editar las rarezas (carga, frecuencia, prefab)

Seleccioná **`Assets/Bateries/BatteryRaritySet.asset`** en el Project y editá el array
`rarities` en el Inspector. Por cada entrada:

| Campo | Qué hace |
|---|---|
| `displayName` | Nombre (informativo / HUD). |
| `rarityIndex` | Número del tipo (0,1,2,3…). **Define el TypeId y el prefab** (ver invariante). Podés tener **la cantidad de tipos que quieras**. |
| `charge` | Cuánta carga suma a la linterna al recogerla. |
| `spawnChance` | **Probabilidad de aparición** (relativa, se normaliza sola). Más alto = más seguido. Para "rara pero potente", `charge` alto + `spawnChance` bajo. |
| `prefab` | El prefab de la pila (debe tener `BatteryEntity` con el mismo `rarityIndex`). |
| `tint` | Color de la pila y de su **luz/glow** en el mundo. |

> `spawnChance` es **relativo** y se normaliza con la suma: no hace falta que sumen 100.
> Con 20/20/20 las tres salen igual; con 60/30/10 → 60% / 30% / 10%.

### 2. Agregar / cambiar un prefab de pila

1. Duplicá un prefab de `prefabs/` (o creá uno nuevo con el modelo que quieras).
2. Agregale el componente **`BatteryEntity`** y seteá su **`rarityIndex`**.
3. Registralo en **`PrefabRegistry.asset`** con **TypeId = 10 + rarityIndex**.
4. Asignalo en la rareza correspondiente del `BatteryRaritySet`.

> No hay tope de tipos: para una 4ª rareza, agregá una entrada al RaritySet con
> `rarityIndex = 3`, un prefab con `BatteryEntity(rarityIndex = 3)` y registralo en el
> `PrefabRegistry` con **TypeId 13**. (El `rarityIndex` es un `byte`, así que soporta hasta
> TypeId 255.)

### 3. Dónde aparecen las pilas (derivación desde el escaneo)

En el componente **`BatterySpawnManager`** (en la escena):

| Parámetro | Default | Qué hace |
|---|---|---|
| `Use Furniture Tops` | true | Pone pilas arriba de los muebles (cubos) escaneados. |
| `Min Furniture Top Area` | 0.05 | Área mínima (m²) de la tapa del mueble para admitir pila. |
| `Surface Offset` | 0.06 | Altura (m) a la que flota la pila sobre la superficie. |
| `Scatter On Floor` | false | Además, esparce pilas por el piso en una grilla. |
| `Floor Only If Clear Above` | true | En el piso, **solo donde no haya un mueble encima**. |
| `Floor Spacing` | 1.5 | Separación (m) de la grilla del piso. |
| `Floor Radius` | 3 | Radio (m) de la grilla alrededor del FloorPoint. |
| `Max Spawn Points` | 12 | Tope de puntos de spawn. |

> El piso requiere que el mapa escaneado tenga un **FloorPoint**. Si no hay muebles ni piso,
> hay un fallback: un anillo de puntos alrededor del anchor.

### 4. Reaparición y recolección

En **`BatterySpawnManager`**:

| Parámetro | Default | Qué hace |
|---|---|---|
| `Respawn Seconds` | 60 | Tiempo de reaparición tras recoger. |
| `Player Block Radius` | 1.5 | Si un jugador está a esta distancia, el timer se **congela**. |
| `Initial Spawn Delay` | 2 | Demora del primer llenado de todos los puntos al arrancar. |
| `Pickup Max Distance` | 2.5 | Distancia máx. a la que el server acepta un pickup. |

En **`BatteryPickupAction`** (componente auto-agregado junto al `ContextActionController`):

| Parámetro | Default | Qué hace |
|---|---|---|
| `Priority` | 100 | Prioridad de la acción (gana a la linterna). |
| `Aim Max Distance` | 2.5 | Distancia máx. para apuntar/recoger. |
| `Aim Angle` | 12 | Semiángulo (°) del cono de apuntado desde el centro. |
| `Aim Check Interval` | 0.1 | Cada cuánto recalcula la pila apuntada (perf). |
| `Block Through Walls` | true | No permite apuntar/recoger a través de paredes/muebles (linecast contra layer `Placed`). |

En **`ContextActionController`**: `Show Action Button` (toggle **maestro** del botón en
pantalla). Además, **cada acción** decide si muestra el botón vía su propio `Show Action
Button`: por defecto la de **recoger pila = sí** y la de **linterna = no** (se prende/apaga
directo con A). El botón aparece solo si el maestro está activo **y** la acción activa lo pide.

La **barra de carga** de la linterna la dibuja **`FlashlightHUD`** (componente aparte, en el
GameObject de la linterna) con su propio toggle `Show Charge Bar`.

### Botón primario (A) y acciones contextuales

El **botón A** del joystick (o el botón en pantalla, o `E` en editor) ejecuta la acción
disponible de **mayor prioridad** según el contexto:

- Si estás **apuntando una pila** → la **recoge** (`BatteryPickupAction`, prioridad 100).
- Si no → **prende/apaga la linterna** (`FlashlightToggleAction`, prioridad 0).

Para **sumar una acción** (abrir puerta, interruptor, etc.): creá un `MonoBehaviour` que
implemente **`IContextAction`** (`Priority`, `ShowActionButton`, `TryResolve(out label)`,
`Execute()`) y ponelo en el **mismo GameObject** que el `ContextActionController` (se
auto-descubre), o registralo por código con `ContextActionController.Instance.Register(...)`.
La de mayor `Priority` disponible gana el botón A; `ShowActionButton` controla si además se
dibuja el botón en pantalla para esa acción.

### 5. Glow de las pilas

Cada pila muestra un **halo billboard aditivo** (shader `Custom/BatteryGlow`, en
`Assets/Bateries/Resources/`) que **emite su propio color**: se ve en oscuridad total, sin
depender de que haya una superficie donde impacte una luz. Es baratísimo (1 quad por pila,
sin iluminación ni sombras). El **color sale del `tint`** de su rareza (vía
`BatteryRaritySet.Current`, que publica el `BatterySpawnManager`).

Se configura en el componente **`BatteryEntity`** del prefab:

| Parámetro | Default | Qué hace |
|---|---|---|
| `Add Glow` | true | Crea el halo. Apagalo si no querés glow en ese tipo. |
| `Glow Size` | 0.35 | Tamaño del halo (m). |
| `Glow Intensity` | 1.6 | Brillo del halo. |
| `Pulse` | true | Latido suave del **tamaño** (la intensidad no se animaría: el aditivo satura). |
| `Occlude By Walls` | true | Atenúa el halo si una pared/mueble (layer `Placed`) tapa la pila. |
| `Occlusion Margin` | 0.25 | Margen (m) sobre el que el halo se **funde de a poco** al asomar por el borde de una pared (distancia fija, no el tamaño de la pila). |
| `Glow Fade Speed` | 8 | Velocidad del fundido (opacidad/seg). |

**Recorte de piso y paredes:**
- **Piso**: el shader recibe la Y (world) del `FloorPoint` (`_FloorY`) y **descarta los
  fragmentos por debajo** — así el halo no chorrea bajo el piso aunque no exista malla de
  piso. Sin `FloorPoint` no recorta.
- **Paredes**: como las paredes escaneadas son transparentes (no escriben depth), la oclusión
  se hace por **linecast** desde la cámara a la pila contra la layer `Placed`. En vez de
  prender/apagar de golpe, se muestrean 5 puntos en un `Occlusion Margin` alrededor de la
  pila y se **funde la opacidad** según la fracción visible (+ un suavizado temporal), así al
  asomar por el borde de una pared el glow aparece gradual, sin salto. Requiere paredes/muebles
  en la layer `Placed` (lo están por defecto al cargar el mapa).

> Perf: unlit aditivo (sin cálculo de luz) + 1 linecast por pila por frame (con un solo
> `Physics.SyncTransforms` por frame). Muy barato. Si querés menos, desactivá `Pulse` u
> `Occlude By Walls`.
>
> El shader vive en `Resources` para garantizar que entre al build (se carga con
> `Resources.Load`); así evita el magenta por *shader stripping* en device. Si igual saliera
> magenta, agregá `Custom/BatteryGlow` a Project Settings → Graphics → Always Included Shaders.

### 6. Linterna (consumo)

En el componente **`Flashlight`** (`Assets/Flashlight.cs`):

| Parámetro | Default | Qué hace |
|---|---|---|
| `Max Charge` | 100 | Carga máxima. |
| `Current Charge` | 100 | Carga actual (se drena con `isOn`). |
| `Drain Per Second` | 2 | Carga consumida por segundo encendida. |

Al llegar a 0 se apaga sola y no se puede encender hasta recoger una pila.

---

## Wiring en la escena (checklist)

1. `BatteryRaritySet.asset` creado y con las rarezas cargadas.
2. Un prefab por rareza con `BatteryEntity` (rarityIndex 0/1/2), registrados en
   `PrefabRegistry.asset` con TypeId 10/11/12.
3. En **SampleScene**: un GameObject con **`BatterySpawnManager`** (asignarle el
   `BatteryRaritySet`) y otro con **`ContextActionController`** (auto-agrega
   `BatteryPickupAction` + `FlashlightToggleAction`; no requiere config extra).
4. La linterna (`Flashlight`) ya está en la escena. Agregale el componente
   **`FlashlightHUD`** para ver la barra de carga.

> Si venías del `BatteryPickupController` anterior: se renombró a `ContextActionController`
> conservando su GUID, así que el componente en la escena sigue apuntando al script nuevo sin
> re-agregar nada (Unity solo recompila).

## Debug

`BatterySpawnManager` tiene un toggle **`Show Debug Hud`** (apagado por defecto) que muestra
en pantalla: si sos server, si arrancó la partida, si `WorldOrigin` está listo, cuántos
puntos hay y cuántas pilas activas, y el motivo si el setup es inválido. **Dejalo apagado en
device** (el `OnGUI` tiene costo).

## Notas de performance

- Los `OnGUI` cachean sus estilos (nada de GC por frame).
- El pickup usa un registro estático `BatteryEntity.Active` (sin `GetComponent` por frame) y
  recalcula el apuntado cada `Aim Check Interval`, no cada frame.
- Las pilas solo actualizan su transform si el anchor se movió.
