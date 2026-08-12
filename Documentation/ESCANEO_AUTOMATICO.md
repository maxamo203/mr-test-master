# Escaneo automático — versión 2

Esta rama contiene únicamente **Scan V2 multivista**. Toma keyframes cuando la
cámara se desplaza o rota, obtiene muestras desde profundidad de AR Foundation y
raycasts, y las fusiona en un volumen disperso de surfels. Después extrae piso y
segmentos de pared, cierra esquinas próximas y crea objetos compatibles con el
editor manual.

V2 no depende de AutoScan V1 ni contiene Atlas V3. Su objetivo es evaluar cuánto
mejora la precisión al observar una superficie desde varias posiciones, manteniendo
procesamiento local, gratuito y sin backend.

Prueba rápida: abrir `ScannerScene`, entrar en Play Mode, pulsar **SCAN V2** y, en
Editor, usar **SIMULAR HABITACIÓN MULTIVISTA**. El escenario automatizado está en
`Mortuorium > QA > Scan V2 > Ejecutar regresión completa`.
