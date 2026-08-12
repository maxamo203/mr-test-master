# Scanner V3 - estrategia QA

## Piramide

### Nivel 1 - matematico determinista

- SE(3), reproyeccion, triangulacion y covariance;
- RANSAC con outliers conocidos;
- pose graph sintetico con loop correcto/falso;
- fusion de profundidad por incertidumbre;
- TSDF, espacio libre y reintegracion;
- topologia de habitaciones y rollback transaccional.

### Nivel 2 - datasets reproducibles

- secuencias sinteticas con ground truth perfecto;
- habitaciones reales medidas;
- ruido de pose/depth inyectable;
- blur, rolling shutter, exposicion y frames faltantes;
- personas/objetos moviles y reflejos;
- regresion contra V1, V2 y oraculo offline.

### Nivel 3 - integracion Unity

- lifecycle, pausa, background y low-memory;
- almacenamiento lleno y bundle corrupto;
- cancelacion en cada etapa;
- domain reload/editor stubs;
- materializacion, undo, JSON, carga y recalibracion;
- cero mutacion manual antes de confirmar.

### Nivel 4 - dispositivos

- matriz iOS/Android con/sin depth;
- sesiones de 1, 5, 10 y 20 minutos;
- temperatura, bateria, FPS y memoria pico;
- interrupciones: llamada, bloqueo, permiso y cambio de orientacion;
- tres operadores y tres recorridos por ambiente.

## Metricas primarias

- error punto-plano mediano/P95;
- error de largo/altura/grosor;
- distancia entre esquinas reales/estimadas;
- precision/recall de superficies y aberturas;
- area de geometria fantasma;
- drift antes/despues del loop closure;
- porcentaje extrapolado;
- cantidad de correcciones manuales;
- tiempo de captura/proceso y tasa de finalizacion.

## Casos adversariales obligatorios

- cuatro paredes blancas;
- espejo de cuerpo entero;
- ventanal y contraluz;
- pasillo largo;
- habitacion no rectangular;
- escalon/desnivel;
- puerta abierta y cerrada;
- persona cruzando repetidamente;
- muebles movidos durante captura;
- retorno al inicio con apariencia repetitiva;
- dos habitaciones visualmente similares para atacar loop closure.

## Criterio de no regresion

V3 puede tardar mas que V2, pero no puede:

- perder contenido manual;
- crear mas falsos positivos;
- usar red;
- bloquear al usuario sin cancelacion;
- consumir almacenamiento sin limite;
- aceptar una optimizacion con residuo peor que la pose AR original.

