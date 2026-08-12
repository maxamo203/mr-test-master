# AutoScan - informe de QA de integracion

Fecha: 11 de agosto de 2026  
Rama: `autoScan`

## Veredicto

La primera etapa es apta para continuar con pruebas fisicas. La solucion mantiene
el modelo del scanner actual: observa planos de manera temporal y solo al finalizar
crea `WallObject` y `FloorPoint` editables, persistibles y compatibles con el flujo
manual. No reemplaza ni limpia contenido creado por el usuario.

No se considera validada para produccion hasta completar la matriz en dispositivos
reales sin LiDAR. El Editor no reproduce la calidad, deriva ni fragmentacion de los
planos entregados por ARKit y ARCore.

## Riesgos encontrados y corregidos

- La estabilidad aumentaba aunque un plano cambiara bruscamente de posicion,
  orientacion o tamano. Ahora exige observaciones consecutivas coherentes.
- El area de pisos irregulares se calculaba con su rectangulo envolvente. Ahora se
  usa el area real del poligono en XZ.
- Un piso automatico podia prevalecer sobre un piso manual. Ahora el piso manual
  conserva su altura y las paredes automaticas se alinean con el.
- Datos no finitos podian contaminar candidatos. Ahora se descartan muestras con
  `NaN` o infinito.
- Era posible intentar finalizar sin resultados utilizables. Ahora `FINALIZAR` se
  habilita solo cuando existe al menos un objeto materializable.
- Cancelar una segunda captura borraba la referencia necesaria para deshacer la
  materializacion anterior. Ahora cancelar no altera el ultimo deshacer valido.
- El contador de objetos listos recalculaba toda la geometria desde IMGUI varias
  veces por frame. Ahora se invalida y recalcula solo cuando cambian observaciones.
- Se reforzo el alta y baja del listener de `ARPlaneManager` para evitar eventos
  duplicados al reactivar el componente.

## Evidencia automatizada

- Compilacion runtime: 0 errores, 0 advertencias.
- Compilacion Editor: 0 errores, 0 advertencias.
- Suite completa EditMode: 41/41 pruebas aprobadas.
- Geometria AutoScan: 11/11 pruebas aprobadas.
- Escenario integrado en `ScannerScene`: PASS.

El escenario integrado verifica:

1. No finalizar sin superficies estables.
2. Cancelar sin modificar el registro.
3. Materializar cuatro paredes y un piso.
4. Conservar esos objetos en `ScanData` tras ida y vuelta JSON.
5. No duplicar una habitacion al repetir el mismo escaneo.
6. Deshacer solo la ultima materializacion.
7. Preservar un piso manual y alinear las paredes con su altura.

Se ejecuta en Play Mode desde:

`Mortuorium/QA/AutoScan/Ejecutar regresion completa`

## Matriz pendiente en dispositivos

Probar al menos un iPhone sin LiDAR y un Android ARCore en:

- habitacion rectangular despejada;
- paredes blancas o con poca textura;
- luz baja y contraluz;
- espejos, ventanas y superficies reflectantes;
- ambiente amueblado y parcialmente ocluido;
- recorrido incompleto y reanudado;
- sesiones largas para medir FPS, memoria, bateria y temperatura;
- guardado, cierre de la app, recalibracion y carga posterior;
- edicion manual de cada pared creada automaticamente.

Registrar por ambiente: tiempo de captura, cantidad de planos observados/estables,
objetos creados, falsos positivos, omisiones, error de dimensiones y correcciones
manuales necesarias. Esos datos deben decidir los umbrales por plataforma antes de
avanzar a reconstruccion densa o profundidad monocular.
