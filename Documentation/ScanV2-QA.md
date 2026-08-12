# Scanner V2 - informe QA

Fecha: 12 de agosto de 2026  
Rama: `addScanerv2`

## Veredicto

El slice V2.1 supera la validacion automatizada de geometria e integracion y queda
apto para iniciar pruebas controladas en dispositivos. No queda aprobado para
produccion hasta validar precision, orientacion de depth, rendimiento y temperatura
en telefonos reales. Windows Editor no provee subsistemas AR y no puede certificar
esas variables.

## Defectos encontrados durante QA y corregidos

1. **Estabilidad falsa por densidad de un solo frame.** Varios pixels del mismo
   keyframe incrementaban el contador temporal de un voxel. Ahora cada voxel cuenta
   como maximo una observacion por keyframe.
2. **Memoria sin limite.** El volumen sparse podia crecer durante toda la sesion.
   Ahora tiene un maximo configurable de 150.000 voxels, conserva actualizaciones de
   celdas existentes y muestra `LIMITE` en la UI al alcanzarlo.
3. **Paredes inventadas sobre huecos grandes.** Fragmentos coplanares separados se
   convertian en una sola pared continua. Ahora se segmentan por continuidad espacial.
4. **Falso piso por cantidad de puntos.** Una franja horizontal angosta podia ganar
   por conteo. Ahora tambien debe cubrir un area minima.
5. **Normales no confiables de feature points.** Se usaba directamente `pose.up`, que
   no representa necesariamente la superficie. Ahora se estima la normal con vecinos
   cercanos o se descarta la muestra.
6. **Fusion incompleta de fuentes.** Si habia environment depth no se incorporaban
   raycasts metricos del mismo keyframe. Ahora ambas fuentes se combinan y el volumen
   las deduplica temporalmente.

## Cobertura ejecutada

### Geometria y limites

- ruido espacial y promedio de posiciones;
- requisito de dos keyframes reales;
- normales con signo invertido;
- rechazo de `NaN` e infinito;
- extraccion de cuatro paredes y piso;
- diferenciacion entre piso y mesa;
- consolidacion de fragmentos coplanares contiguos;
- separacion de fragmentos con hueco grande;
- cierre de esquinas perpendiculares cercanas;
- rechazo de superficie horizontal demasiado angosta;
- limite duro de memoria del volumen.

### Integracion con el producto

- no iniciar dos capturas simultaneas;
- finalizar sin datos no cierra ni modifica la captura;
- cancelar no crea objetos;
- paredes generadas tienen malla y `MeshCollider`;
- ida y vuelta JSON mediante `ScanData`;
- segundo escaneo identico no duplica paredes;
- undo sobrevive a una captura posterior cancelada;
- cancelar y undo repetidos son idempotentes;
- pared manual previa sobrevive materializacion y undo;
- piso manual conserva identidad y altura;
- objetos automaticos se eliminan sin tocar contenido manual.

### Regresion y repeticion

- Scanner V2: 10/10 pruebas especificas aprobadas.
- Proyecto completo: 51/51 pruebas EditMode aprobadas.
- Escenario integrado `ScannerScene`: PASS.
- Escenario integrado repetido veinte veces: PASS x20.
- Compilacion runtime y Editor: 0 errores, 0 advertencias.
- La suite PlayMode del proyecto no contiene tests registrados; el escenario QA en
  Play Mode cubre actualmente la integracion runtime.

## Riesgos que requieren dispositivo

1. Correspondencia de orientacion, recorte e intrinsecos entre environment depth y
   la camara en iOS/Android.
2. Calidad de ARCore Depth en paredes blancas, vidrio, espejos y poca luz.
3. Disponibilidad real de depth en modelos Android diferentes.
4. Fragmentacion de raycasts y feature points en iPhone sin LiDAR.
5. FPS, memoria nativa, bateria y temperatura en recorridos de 5 y 10 minutos.
6. Deriva del `WorldOrigin` antes y despues de recalibrar.
7. Error metrico de largo, altura, esquina y paralelismo frente a medicion real.

## Protocolo minimo de aceptacion fisica

Usar una habitacion medida con cinta/laser y repetir tres recorridos por dispositivo.
Aceptar V2.1 solo si:

- error mediano de paredes <= 8 cm;
- error P95 <= 15 cm;
- ninguna pared fantasma mayor a 50 cm;
- cero perdida de objetos manuales;
- al menos 24 FPS durante captura;
- sin cierre por memoria ni degradacion termica severa en 10 minutos;
- guardado, cierre, recalibracion y carga conservan la geometria.

Dispositivos minimos: iPhone 16 sin LiDAR, un iPhone Pro con LiDAR y dos Android
ARCore, uno con Depth API y otro usando el fallback disponible.

## Como repetir

En `ScannerScene`, Play Mode:

`Mortuorium/QA/Scan V2/Ejecutar regresion completa`
