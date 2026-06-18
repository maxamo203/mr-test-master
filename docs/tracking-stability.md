# Estabilidad de tracking AR — especificación técnica

**Branch:** `feature/tracking-stability`  
**Contexto:** mejora de robustez para gameplay de horror en espacios interiores

---

## Problema observado

Cuando el usuario agita el celular apuntando a una pared que ocupa toda la cámara, la geometría escaneada (paredes, cubos) aparece congelada en el espacio y desincronizada del movimiento físico de la cámara. Al alejarse, la escena se autocorrige sin necesidad de recalibrar.

## Causa raíz

ARCore y ARKit usan SLAM visual: estiman la pose de la cámara triangulando **feature points** (puntos visuales con alto contraste y textura). Una pared lisa llenando el frame no tiene features detectables. El sistema cae a **IMU-only** (giroscopio + acelerómetro), que acumula deriva en fracciones de segundo con movimiento brusco.

El resultado visible: la estimación de pose de cámara se congela o salta, pero `WorldOrigin` sigue parentado al `ARAnchor` cuya pose también queda con datos desactualizados. La geometría AR aparece "flotando" fija en el aire en lugar de seguir al observador.

Cuando la cámara vuelve a ver el entorno, SLAM recupera features, corrige la pose acumulada con loop-closure, y la escena salta de vuelta a su posición correcta.

---

## Solución implementada

### TrackingGuard — nuevo componente (`Assets/AR/TrackingGuard.cs`)

Monitorea el estado del `ARSession` cada frame. Cuando el tracking pasa a degradado, **desparentamos `WorldOrigin` del anchor y lo sostenemos fijo en espacio mundo**. Cuando el tracking se recupera, lo **re-parentamos suavemente** con interpolación para evitar saltos visibles.

#### Estados internos

```
Tracking OK  →  WorldOrigin parentado al anchor (comportamiento actual)
              ↓  NotTrackingReason: InsufficientFeatures | ExcessiveMotion
Tracking MAL →  WorldOrigin desparentado, pose fija en última posición buena
              ↓  ARSession vuelve a SessionTracking
Recovering   →  WorldOrigin lerp desde posición congelada → posición del anchor
              ↓  lerp completo (threshold < 1 cm)
Tracking OK  →  WorldOrigin re-parentado al anchor
```

#### Parámetros configurables (Inspector)

| Parámetro | Default | Descripción |
|---|---|---|
| `degradedThreshold` | 0.5 s | tiempo mínimo en estado degradado antes de freezar (evita micro-blips) |
| `recoveryLerpSpeed` | 3.0 | velocidad de re-snap al anchor al recuperar (unidades/seg) |
| `showDebugUI` | true en dev | indicador en pantalla del estado actual |

### Cambios en WorldOrigin (`Assets/AR/WorldOrigin.cs`)

Se agrega API mínima:
- `Freeze()` — guarda pose mundo actual, desparentea del anchor
- `Unfreeze(Transform anchor)` — inicia la interpolación de vuelta al anchor y re-parentea al completar

`SetOrigin` no cambia: sigue siendo el punto de entrada para calibración inicial y recalibración manual.

### Sin cambios en ScannerSceneBootstrap ni ScanStateMachine

`TrackingGuard` se auto-conecta en `Awake` buscando `ARSession` y `WorldOrigin` en la escena. No requiere orden de ejecución forzado porque actúa después de que ambos están inicializados.

---

## Lo que NO hace esta PR

- **Matching de planos vs. paredes escaneadas** (idea descartada en esta iteración por alto costo computacional y complejidad de matching)
- **Múltiples imágenes de referencia** — el código ya lo soporta via `MutableRuntimeReferenceImageLibrary`; queda como mejora futura independiente
- **Cambios al flujo de calibración** — el botón de recalibrar y `ARImageAnchor` no se tocan

---

## Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `Assets/AR/TrackingGuard.cs` | **Nuevo** |
| `Assets/AR/WorldOrigin.cs` | Agrega `Freeze()` / `Unfreeze()` |

---

## Consideraciones para el reviewer

- `Freeze()` / `Unfreeze()` usan `worldPositionStays: true` en `SetParent`, igual que el path `keepVisualPosition = true` de `SetOrigin`. Es el mismo patrón ya probado en la recalibración manual.
- El `degradedThreshold` existe para no reaccionar a pérdidas de tracking de <0.5 s que ARCore autocorrige solo, evitando re-parents innecesarios.
- En editor, `ARSession` no existe (AR stubbeado). `TrackingGuard` chequea `#if UNITY_EDITOR` y se deshabilita solo, sin afectar el flujo del editor stub existente en `ARImageAnchor`.
- Si `WorldOrigin` aún no está listo (`IsReady == false`), `TrackingGuard` no hace nada — no tiene sentido freezar antes de la primera calibración.
