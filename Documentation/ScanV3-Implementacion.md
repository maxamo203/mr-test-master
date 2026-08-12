# Scanner V3 Atlas - implementacion actual

Fecha: 12 de agosto de 2026  
Rama: `addScannerV3`

## Vertical funcional entregado

- modo independiente `ScanV3_Capturing` y boton `ATLAS V3`;
- captura local de luminancia e imagen JPEG reducida;
- gate de exposicion, nitidez, tracking, movimiento, giro e intervalo;
- profundidad nativa mas raycasts/feature points heredados de V2;
- observaciones guardadas relativas a cada camara;
- bundle incremental con manifest liviano y sidecars atomicos por keyframe;
- recuperacion automatica del ultimo bundle incompleto;
- descriptor visual compacto para proponer cierres de bucle;
- verificacion de apariencia, pose y solapamiento geometrico antes de aceptar loops;
- grafo de poses robusto con nodo origen fijo y rollback si aumenta el residuo;
- reintegracion de todas las vistas con las poses optimizadas;
- extraccion estructural reutilizando el volumen y QA endurecido de V2;
- deduplicacion frente a paredes/piso manuales;
- materializacion transaccional con validacion de collider y JSON;
- rollback completo ante una falla parcial;
- cancelacion borra evidencia temporal; exito tambien limpia el bundle;
- undo sobrevive a una captura posterior cancelada.

## Fuera de este vertical

Continuan planificados y no se presentan como terminados:

- matcher local ORB/AKAZE/OpenCV y PnP/RANSAC real;
- bundle adjustment de landmarks ademas del pose graph compacto;
- profundidad monocular Sentis/ExecuTorch;
- covariance por fuente y fusion probabilistica completa;
- TSDF con espacio libre, voxel hashing y marching cubes;
- semantica de puertas/ventanas y enmascarado de personas;
- render inverso y editor de propuestas por confianza;
- benchmarks y tuning fisico por dispositivo.

El grafo actual mejora drift distribuido y solo acepta un loop cuando descriptor,
pose aproximada y nubes observadas son compatibles. No sustituye aun un frontend
visual SLAM completo.

## Evidencia QA

- 6/6 pruebas Atlas de vision, grafo, loop y bundle;
- 57/57 pruebas EditMode del producto;
- escenario integrado Atlas: PASS;
- escenario integrado repetido diez veces: PASS x10;
- fallo parcial inyectado: rollback sin residuos y bundle recuperable;
- compilacion runtime/Editor: 0 errores y 0 advertencias.

## Prueba manual en Editor

1. Abrir `ScannerScene` y entrar en Play Mode.
2. Completar/simular calibracion.
3. Seleccionar `ATLAS V3`.
4. Usar `SIMULAR BUNDLE ATLAS`.
5. Presionar `FINALIZAR`.

Regresion automatizada:

`Mortuorium/QA/Scan V3 Atlas/Ejecutar regresion completa`

