# Scanner V2 - roadmap de reconstruccion multivista local

## Objetivo

Modelar automaticamente un ambiente con mayor precision que la deteccion aislada
de planos, sin backend, sin cuenta, sin Internet y sin exigir LiDAR. La salida debe
seguir siendo el modelo editable actual (`WallObject`, `CubeObject`, `FloorPoint`,
`DoorData` y `MarkerObject`) y guardarse exclusivamente mediante `SceneRegistry`.

Scanner V2 es aditivo: no reemplaza el escaneo manual ni AutoScan V1.

## Arquitectura elegida

1. Seleccionar keyframes cuando la camara se desplaza o rota lo suficiente.
2. Obtener profundidad metrica desde proveedores intercambiables:
   - environment depth de AR Foundation cuando el dispositivo la soporte;
   - raycasts de profundidad, planos y feature points como fallback universal;
   - profundidad monocular local calibrada en una etapa posterior.
3. Transformar cada medicion al espacio local de `WorldOrigin`.
4. Fusionar observaciones repetidas en un volumen sparse de surfels.
5. Extraer piso y paredes desde la evidencia consolidada.
6. Materializar objetos normales del scanner solo al confirmar.

La IA nunca sera la unica fuente de escala. Sus predicciones se calibraran contra
poses AR, raycasts metricos, la imagen de referencia y profundidad nativa disponible.

## Fases

### V2.1 - Pipeline metrico multivista (implementado)

- Modo independiente `ScanV2_Capturing`.
- Seleccion de keyframes por distancia, angulo y tiempo minimo.
- Fuente de environment depth con lectura `DepthFloat32`/`DepthUint16`.
- Fallback por grilla de raycasts AR: Depth, planos y feature points.
- Conversion de puntos y normales a coordenadas de `WorldOrigin`.
- Fusion sparse configurable por voxel y cantidad de observaciones.
- Una superficie solo gana estabilidad con keyframes distintos, no por densidad de pixels.
- Limite configurable de memoria del volumen y aviso visible al alcanzarlo.
- Deteccion de piso por consenso horizontal.
- Agrupacion de paredes coplanares por normal y distancia.
- Separacion de tramos coplanares cuando existe un hueco fisico grande.
- Cierre de esquinas cercanas por interseccion de paredes consolidadas.
- Supresion de duplicados con paredes existentes.
- Preservacion y precedencia del piso manual.
- Cancelacion sin efectos y undo de la ultima materializacion.
- UI, simulador de habitacion y escenario de regresion en Editor.

### V2.2 - Profundidad monocular local

- Incorporar Unity Sentis sin activar red ni telemetria.
- Evaluar Depth Anything V2 Small metric indoor y modelos tiny equivalentes.
- Convertir y cuantizar el modelo con licencia redistribuible.
- Ejecutar solo en keyframes, con resolucion y frecuencia adaptativas.
- Estimar escala/shift usando muestras metricas sparse de la misma imagen.
- Rechazar frames con residuo de calibracion alto o tracking deficiente.
- Mantener el proveedor separado para poder cambiar de modelo sin tocar fusion/UI.

**Gate:** error mediano de profundidad menor a 8 cm entre 0.5 y 4 m en el set de
habitaciones objetivo; sin ese gate la prediccion no ingresa al volumen.

### V2.3 - Fusion densa TSDF

- Sustituir o complementar surfels con bloques TSDF sparse.
- Integrar espacio libre y superficies, no solo puntos observados.
- Limitar memoria por cantidad de bloques y radio alrededor del usuario.
- Extraer malla temporal con marching cubes en segundo plano.
- Visualizar cobertura y zonas con baja confianza.

**Gate:** memoria estable en sesiones de 10 minutos y al menos 24 FPS durante captura.

### V2.4 - Optimizacion estructural

- Ajuste Manhattan/Atlanta de direcciones dominantes.
- RANSAC robusto y refinamiento global de planos.
- Interseccion de paredes para cerrar esquinas.
- Estimacion conjunta de piso, techo, altura y grosor.
- Deteccion de cajas orientadas para muebles/obstaculos.
- Puntaje de confianza y revision individual antes de materializar.

### V2.5 - Semantica local

- Detector movil local para puertas y ventanas.
- Fusion temporal de detecciones sobre paredes estructurales.
- Puertas confirmadas como `DoorData`; ventanas como `MarkerObject`.
- Casos inciertos quedan como sugerencias, nunca modifican geometria automaticamente.

### V2.6 - Productizacion

- Pausa/reanudacion y guardado de captura temporal recuperable.
- Presupuesto termico, memoria y bateria por plataforma.
- Perfiles automaticos de calidad para iOS y Android.
- Tutorial guiado por cobertura real, no por tiempo.
- Comparador V1/V2 y telemetria local exportable para QA.

## Criterios de aceptacion

- Cero solicitudes de red durante captura, fusion y materializacion.
- Funciona con fallback en iPhone sin LiDAR y Android AR Foundation.
- Nunca llama `SceneRegistry.ClearAll()` en el flujo de usuario.
- Inicio, cancelacion y finalizacion preservan todo contenido manual existente.
- Un segundo recorrido equivalente no duplica paredes.
- Toda salida puede seleccionarse, editarse, eliminarse, guardarse y cargarse.
- Datos temporales no se incorporan a `ScanData`.
- La precision se informa con error metrico, no solo apreciacion visual.

## Matriz de validacion fisica

Medir en iPhone sin LiDAR, iPhone Pro con LiDAR y al menos dos Android ARCore:

- habitacion rectangular despejada;
- paredes blancas y con poca textura;
- ambiente amueblado y oclusiones parciales;
- espejos, ventanas y superficies brillantes;
- luz baja, contraluz y movimiento rapido;
- recorrido incompleto y segundo recorrido complementario;
- guardado, cierre, recalibracion y carga posterior.

Por prueba registrar: dimensiones reales, dimensiones estimadas, error por pared,
error de esquina, tiempo, keyframes, fuente usada, voxels, objetos omitidos,
duplicados, FPS minimo, memoria maxima, temperatura y correcciones manuales.

## Ejecucion de QA en Editor

Abrir `ScannerScene`, entrar en Play Mode y ejecutar:

`Mortuorium/QA/Scan V2/Ejecutar regresion completa`
