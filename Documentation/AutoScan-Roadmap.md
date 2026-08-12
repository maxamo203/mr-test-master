# AutoScan — roadmap de implementación local

## Objetivo

AutoScan observa el ambiente desde la cámara del teléfono y propone los mismos
objetos que hoy se crean manualmente. Al finalizar, cada resultado es un
`WallObject`, `CubeObject`, `DoorData`, `MarkerObject` o `FloorPoint` normal:
se selecciona, edita, elimina, guarda y carga con los flujos existentes.

La funcionalidad es aditiva. No reemplaza builders, no vacía `SceneRegistry`,
no cambia `ScanData` y no necesita backend, cuenta ni conexión a Internet.

## Principios de integración

1. Las observaciones crudas son temporales y nunca se serializan.
2. Sólo se materializan objetos de dominio mediante las fábricas actuales.
3. Los objetos manuales existentes no se modifican ni eliminan.
4. La salida se guarda exclusivamente mediante `SceneRegistry.Capture()`.
5. Cada etapa debe funcionar sin las etapas posteriores.
6. Los modelos que se incorporen deben ejecutarse dentro del dispositivo y
   permitir distribución gratuita con el producto.

## Roadmap

### R1 — Planos automáticos compatibles con el editor

- Modo `AutoScan_Capturing` independiente de las herramientas manuales.
- Captura incremental de `ARPlane` horizontales y verticales.
- Indicador de cobertura según orientaciones observadas por la cámara.
- Previsualización de cada superficie sin registrarla en la escena guardable.
- Estabilización por cantidad de observaciones.
- Consolidación de fragmentos coplanares y solapados.
- Detección del piso dominante.
- Conversión a `WallObject` y `FloorPoint`.
- Supresión de duplicados respecto de objetos manuales existentes.
- Retorno a `Idle` para revisión y edición manual.

**Salida:** habitación básica automática, local y guardable con `ScanData v1`.

### R2 — Profundidad monocular local

- Integrar `Depth Anything V2 Small` con Unity Sentis.
- Ejecutar inferencia espaciada y con resolución configurable.
- Ajustar profundidad relativa a escala métrica con planos, raycasts y la
  referencia física de la sesión.
- Rechazar frames con tracking deficiente o movimiento excesivo.
- Medir FPS, memoria y temperatura en iPhone 16 y Android sin sensor de profundidad.

**Salida:** mapas de profundidad métricos temporales sin servicios externos.

### R3 — Reconstrucción densa temporal

- Volumen TSDF por bloques en espacio de `WorldOrigin`.
- Integración de profundidad y pose de cámara.
- Límites de memoria y descarte de bloques lejanos/no confiables.
- Malla temporal para visualización y diagnóstico.
- Extracción de planos desde la reconstrucción acumulada.

**Salida:** superficies más completas que las entregadas por AR Foundation.

### R4 — Interpretación estructural

- Restricciones Manhattan/Atlanta para estabilizar direcciones.
- Intersección de planos y cierre de esquinas.
- Agrupación de paredes conectadas mediante `PolylineId`.
- Estimación conjunta de piso, techo, altura y grosor.
- Cajas orientadas para muebles y obstáculos.

**Salida:** `WallObject` y `CubeObject` editables con menos fragmentación.

### R5 — Aberturas y semántica local

- Usar clasificaciones nativas cuando estén disponibles.
- Detector/segmentador móvil local para puerta y ventana.
- Fusión temporal de máscaras sobre las paredes.
- Puertas confirmadas como `DoorData`.
- Ventanas confirmadas como `MarkerObject` del catálogo actual.
- Umbral de confianza: los casos dudosos quedan como sugerencias revisables.

**Salida:** estructura semántica compatible con las historias del escáner.

### R6 — Calidad de producto

- Tutorial de movimiento y mapa de zonas no observadas.
- Pausa/reanudación de captura.
- Deshacer sólo los objetos creados por la última materialización.
- Pruebas prolongadas, poca luz, espejos, paredes sin textura y habitaciones
  con muebles.
- Validación de guardado/carga y recalibración en una sesión posterior.

## Criterios de aceptación globales

- Funciona en iPhone sin LiDAR y en Android compatible con AR Foundation.
- No realiza solicitudes de red ni contiene credenciales.
- AutoScan nunca llama `SceneRegistry.ClearAll()`.
- Los objetos manuales previos sobreviven al inicio, cancelación y finalización.
- El JSON guardado usa exclusivamente los DTO actuales de `ScanData`.
- Después de finalizar, todos los resultados se editan con los handles actuales.
- Cargar un escaneo automático usa exactamente `ScanLoader.Load()`.

