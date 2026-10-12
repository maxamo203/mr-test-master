# Audio dinámico de Sorken y ambiente — implementación

## Referencias migradas

| Uso | Antes | Ahora |
|---|---|---|
| Pisadas de Sorken | `paso_01.ogg` a `paso_06.ogg`, por temporizador | `paso_entrada_puerta_01.wav` a `04.wav`, un clip por apoyo de animación |
| Entrada por puerta | pista de tipo al aparecer + `sorkenEntra` al perseguir | `entrada_puerta_crujido.wav`, una vez al comenzar `EmergingDoor` |
| Entrada por ventana | pista de tipo del catálogo | Se conserva sin cambios |
| Golpes ambientales | No existían | `golpes_puerta_01.wav` o `02.wav`, nunca juntos |
| Manija ambiental | No existía | `manija_puerta_01.wav`, evento independiente |

Los clips anteriores permanecen en el proyecto como respaldo, pero ya no están asignados a
la pista activa de pasos.

## Pisadas

- La fase del `Playable` es el reloj; la cercanía sólo queda a cargo de la atenuación 3D.
- Perfiles normalizados configurados en `SorkenGameplay.prefab`:
  - Persecución: 12 contactos.
  - Marcha cubierta: 5 contactos.
  - Entrada por puerta: 4 contactos.
  - Retirada: 2 contactos.
- Se reproduce como máximo el contacto más reciente ante un salto grande de fase.
- Reinicios, cambios de estado y wrap del loop no generan pisadas atrasadas.
- La posición usa el pie izquierdo/derecho del rig; si no se encuentra, usa la base del Sorken.
- Las cuatro variantes se eligen sin repetición inmediata.

## Ambiente

- El host decide evento, variante, puerta, secuencia y momento; los clientes reciben la
  misma decisión y posición relativa al anchor.
- Golpes: presupuesto aleatorio de 1 o 2 por noche.
- Manija: máximo 1 por noche, independiente; probabilidad de 30% en una ventana elegible.
- Cada oportunidad exige 5–10 segundos continuos sin amenazas relevantes.
- Sorken, Veleth, Arbmos visible, libro consumiéndose, pausa o tracking no fiable reinician
  la ventana de silencio.
- Sólo se usan marcadores `Door` reales. Sin puerta elegible, el evento se posterga.
- Golpes y manija comparten una fuente dedicada: no se superponen y se apagan con fade si
  comienza una amenaza.
- La atenuacion 3D por proximidad se combina con variacion por reproduccion: pasos hasta
  22%, golpes 12%, y manija/crujido 8%. El volumen calibrado es siempre el techo.
- Los eventos ambientales no crean entidades ni modifican daño o cordura.

## Pendiente de calibración audiovisual

- Revisar visualmente los contactos normalizados en dispositivo y ajustar si el error supera
  aproximadamente 80 ms.
- Calibrar volumen y alcance en habitaciones reales con auriculares y altavoz de teléfono.
- No se agregó impacto de aterrizaje por ventana porque no existe un asset específico.
- Las licencias de los sonidos aportados siguen pendientes de verificación.
