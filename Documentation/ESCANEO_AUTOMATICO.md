# Escaneo automático — versión 3 Atlas

Esta rama contiene únicamente **Atlas V3**. Captura keyframes con evidencia visual
y profundidad en un bundle local recuperable, descarta cuadros oscuros, quemados o
borrosos, detecta cierres de recorrido y optimiza un grafo de poses antes de volver
a integrar las muestras en su propio volumen disperso de surfels.

La materialización es transaccional: propone piso y paredes compatibles con el
editor manual, evita duplicar contenido existente y revierte todo si falla una parte.
No depende de AutoScan V1 ni de Scan V2, no usa backend y no requiere entrenamiento.

Prueba rápida: abrir `ScannerScene`, entrar en Play Mode, pulsar **ATLAS V3** y, en
Editor, usar **SIMULAR BUNDLE ATLAS**. El escenario automatizado está en
`Mortuorium > QA > Scan V3 Atlas > Ejecutar regresión completa`.
