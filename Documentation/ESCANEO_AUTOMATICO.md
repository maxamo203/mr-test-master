# Escaneo automático — versión 1

Esta rama contiene únicamente **AutoScan V1**. Observa continuamente los hits de
AR Foundation, agrupa muestras cercanas y estabiliza planos mediante acumulación
temporal. Al finalizar propone piso y paredes y los materializa usando los mismos
`WallObject`, `FloorPoint` y `SceneRegistry` del editor manual.

Es la alternativa más simple y liviana: no guarda keyframes, no fusiona profundidad
en un volumen y no optimiza la trayectoria. Sirve como línea base para medir rapidez,
cobertura y falsos positivos frente a V2 y V3.

Prueba rápida: abrir `ScannerScene`, entrar en Play Mode, pulsar **AUTO ESCANEO** y,
en Editor, usar **SIMULAR HABITACIÓN EN EDITOR**. El escenario automatizado está en
`Mortuorium > QA > AutoScan > Ejecutar escenario completo`.
