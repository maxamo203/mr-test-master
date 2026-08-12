# Scanner V3 - roadmap con gates

## Regla de avance

Ninguna etapa se integra al flujo principal por verse mejor. Cada fase debe superar
un gate cuantitativo frente a Scanner V2 y mantener cancelacion, privacidad y
compatibilidad con `SceneRegistry`.

## Fase 0 - banco de medicion

- Construir 6 ambientes de referencia medidos con laser/cinta.
- Capturar datasets repetibles en iPhone 16, iPhone Pro y dos Android.
- Herramienta para exportar evidencia QA sin datos personales innecesarios.
- Metricas automaticas: error de plano, esquina, escala, completitud y fantasmas.
- Baseline formal de Scanner V1/V2.

**Gate:** dataset versionado, protocolo repetible y reporte automatico.

## Fase 1 - bundle y captura activa

- `ScanCaptureV3` temporal, incremental y recuperable.
- Keyframes con pose, intrinsecos, calidad, RGB y depth opcional.
- Deteccion de blur, exposicion, tracking y redundancia.
- UI de cobertura por zona/direccion.
- Pausa, reanudacion, cancelacion y limpieza.

**Gate:** 100% de recorridos QA sobreviven pausa/cierre; menos de 30% de keyframes
redundantes; cero cambios en `SceneRegistry` antes de confirmar.

## Fase 2 - grafo y cierre de bucle

- Descriptores globales locales.
- Matching + RANSAC de candidatos.
- Pose graph robusto con priors AR y referencia fisica.
- Bundle adjustment y rollback ante divergencia.
- Visualizador de residuales y loops aceptados/rechazados.

**Gate:** deriva de cierre reducida al menos 40% sin empeorar ningun dataset mas de
2 cm; cero falsos loops en suite adversarial.

## Fase 3 - profundidad probabilistica

- Contrato `depth + variance + mask + provenance`.
- Depth nativo, triangulacion y raycasts.
- Modelo monocular local cuantizado.
- Calibracion scale/shift y rechazo por residuo.
- Enmascarado de zonas dinamicas.

**Gate:** error mediano <= 6 cm entre 0.5 y 4 m; P95 <= 12 cm; modelo neural nunca
degrada una medicion nativa confiable.

## Fase 4 - TSDF reintegrable

- Voxel hashing, espacio libre, pesos y presupuesto LRU.
- Reintegration tras optimizar poses.
- Marching cubes y preview progresivo.
- Fallback automatico a surfels V2.

**Gate:** <= 350 MB adicionales en perfil baseline; >= 24 FPS durante captura;
procesamiento final <= 45 s para una habitacion estandar.

## Fase 5 - modelo estructural

- RANSAC de planos y restricciones Manhattan/Atlanta adaptativas.
- Piso, techo, paredes, grosor y esquinas.
- Obstaculos como OBB/hulls.
- Aberturas geometricas.
- Confianza y provenance por entidad.

**Gate:** error mediano de pared <= 5 cm, esquina <= 8 cm, completitud >= 95%,
cero pared fantasma mayor a 40 cm en datasets de aceptacion.

## Fase 6 - semantica y dinamicos

- Puerta/ventana/mueble mediante modelo local pequeno.
- Fusion temporal y consistencia geometrica.
- Mascara de personas, mascotas y objetos moviles.
- Sugerencias para baja confianza.

**Gate:** precision >= 95% para puertas/ventanas confirmadas; casos dudosos nunca se
materializan sin revision.

## Fase 7 - verificacion y editor

- Render inverso contra todos los keyframes.
- Heatmap de error y cobertura.
- Modelo fantasma con aceptacion por entidad.
- Materializacion transaccional y rollback.
- Guardado/carga/recalibracion con DTO actuales.

**Gate:** 500 ciclos materializar-deshacer sin perdida; JSON estable; toda entidad
aceptada editable con handles/colliders existentes.

## Fase 8 - productizacion

- Perfiles termicos y de memoria por dispositivo.
- Resume tras interrupcion y almacenamiento insuficiente.
- Accesibilidad/tutorial.
- Auditoria de red y licencias.
- Piloto longitudinal en habitaciones reales.

**Gate de release:** 95% de sesiones completadas sin crash; cero trafico de red;
10 minutos sin degradacion termica severa; mejora estadisticamente significativa
frente a Scanner V2.

## Orden recomendado de implementacion

1. Fase 0 y Fase 1: sin dataset no se puede demostrar mejora.
2. Fase 2: corregir poses antes de invertir en reconstruccion densa.
3. Fase 3 con fuentes metricas; neural entra ultimo dentro de la fase.
4. Fase 4 y Fase 5.
5. Editor/verificacion antes de semantica completa.
6. Semantica y productizacion.

Estimacion orientativa para un equipo de 2-3 personas especializadas: 7-11 meses
hasta piloto robusto. Un prototipo de Fases 0-3 puede lograrse en 10-14 semanas.

