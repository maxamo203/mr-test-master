# Implementación de audio acordada el 13/10/2026

Estado de integración: completada en la rama `sound-changes` el 14/10/2026.

## Lo anterior que ya estaba correcto

- Los dos golpes de puerta y la manija son eventos independientes, elegidos por el host y
  replicados con variante y posición exactas. Exigen una ventana continua de 5–10 segundos
  sin eventos y no se concatenan entre sí.
- El crujido de puerta de Sorken se dispara al comenzar la entrada real por puerta. Las
  pisadas separadas siguen contactos de la animación, son espaciales, varían en intensidad
  por proximidad y conservan la ganancia contundente acordada.
- Los susurros del libro ya usaban un loop posicional asociado al libro y se mantenían
  durante la defensa. Se conservó esa arquitectura.

## Implementado en esta entrega

- **Arbmos:** exhalación intermitente; resonancia entrada → sostenido → salida natural con
  crossfade de 150 ms; tres enfados sin repetición inmediata; cuatro pasos disparados por
  contactos de su propia animación; jadeo local para agarre no letal y primera onda local
  para captura fatal. Las copias ocultas de otros jugadores no generan audio.
- **Fondo y derrota:** `Dark Choir Singing` reemplaza el fondo nocturno. La muerte ya no
  cambia a la risa/música antigua. El stinger suena una vez en una voz aparte y atenúa el
  fondo temporalmente; el fondo conserva clip, fuente y posición al morir, espectar o
  volver al game over.
- **Libro:** eliminado el disparo antiguo de inicio. El asset activo se reemplazó por el
  derivado final con susurros superpuestos y fondo tenue ×2; suena mientras el consumo está
  en progreso, también cuando la defensa hace retroceder la oscuridad.
- **Veleth:** retirado el generador/sonido antiguo de invocación. La emergencia reproduce
  una vez la capa orgánica de 8,3 s y la cancela suavemente al pasar a persecución; apoyos y
  roces se disparan por perfiles de contacto; la persecución agrega el loop espectral; la
  captura fatal usa el scream local dedicado. Se impidió el reinicio del clip de emergencia
  en el último frame de la fase.
- **Sorken:** la primera cobertura alterna dos reacciones de 3 s por intento. Las cinco
  vocalizaciones aparecen sólo durante la marcha cubierta, con cadencia y memoria de
  variante independientes de las pisadas. La captura fatal usa el scream local dedicado.
- **Infraestructura:** se agregaron voces cancelables de fase, voces dedicadas para muerte y
  resonancia, catálogo reproducible, importación mono/streaming según uso y limpieza de
  loops/transitorios ante transición o despawn. No se modificaron tiempos ni reglas de
  gameplay para adaptar audios.
- **Procedencia:** `Assets/Audio/ATTRIBUTIONS.md` documenta fuentes, licencias y edición.
  Sólo derivados finales están referenciados por el catálogo; fuentes, demos y alternativas
  permanecen fuera de la reproducción del juego.

## Validación ejecutada

- Unity 6000.4.4f1 compiló el proyecto y ejecutó
  `AudioDecisions20261013Setup.Apply` sin errores.
- Test Runner EditMode: **187/187 pruebas aprobadas**, incluidas las nuevas comprobaciones
  de catálogo, cantidad de variantes, nombres y duración de Veleth/libro.
- Los **37 derivados activos** se decodificaron completos con FFmpeg sin errores.
- Los hashes SHA-256 de los 37 assets integrados coinciden con sus derivados finales en
  `output/`; el libro coincide con `libro_susurros_superpuestos_fondo_x2_loop.wav` aunque
  conserva su ruta/GUID de Unity para no romper referencias.
- Búsqueda de referencias activas: no quedan usos de los slots antiguos de muerte,
  invocación Veleth, inicio de consumo ni agarres antiguos.

## Validación pendiente en dispositivo

- No se realizó escucha perceptual en teléfono/auriculares ni una partida multijugador
  host/cliente en esta ejecución. Falta calibrar niveles, unión audible de loops, localización
  espacial y correspondencia exacta de contactos con las animaciones reales.
- Deben probarse en dispositivo: pausa/reanudación, interrupción de cada fase, tracking,
  captura, reintento, alternancia game over/espectador y continuidad host/clientes.
- Los perfiles de contacto de Arbmos y Veleth se dedujeron de las animaciones actuales;
  pueden necesitar ajuste fino visual/auditivo sin alterar la mecánica.
- La procedencia original del audio aportado para el libro y de los audios aportados de
  puerta/manija/pasos sigue pendiente de confirmación antes de una publicación pública.
