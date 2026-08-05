# Mortuorium — Tareas pendientes (Current Sprint)

Backlog del doc `Grupo112_Documentacion_v6.0` que **todavía no está implementado** en el
proyecto, auditado contra el código el 2026-08-05. Pensado para volcarse como tarjetas
de Trello en el tablero "Mortuorium - Tareas", columna **Current Sprint**.

Las épicas 4, 5, 6 (Sorken, Cordura, Arbmos) y 8 (Noches y progresión) están completas
y no figuran acá.

---

## Épica 1 — Plataforma y hardware

### Chequeo de compatibilidad y versión mínima de SO
Al primer arranque, verificar Android >= 14 / iOS >= 17. Si no cumple, popup bloqueante:
"Lo sentimos, la versión de su dispositivo no es compatible con el juego."
Hoy no hay ningún gate de versión en el bootstrap. — *US-1.1 CA1, prioridad Alta*

### Aviso de dispositivo óptimo
Informar al jugador que iOS da mejor estabilidad de tracking que Android, sin bloquear
el juego. — *US-1.5, prioridad Baja*

---

## Épica 2 — Escaneo de espacio físico

### Cadencia mínima de 30 s entre recalibraciones
La resincronización espacial no debe re-habilitarse hasta pasados 30 s de la anterior,
para evitar procesamiento innecesario. Falta verificar/implementar el cooldown.
— *US-2.1 CA2, prioridad Alta*

---

## Épica 3 — Sistema de linterna y batería

### Verificar umbrales de respawn de pilas
La lógica base existe (`BatteryEntity`, `BatterySpawnManager`), pero falta confirmar
los dos criterios del doc: no reaparecer en el mismo punto antes de 1 minuto, y no
generar si el jugador está a menos de 1 m del punto. — *US-3.6 / US-3.7, prioridad Media*

---

## Épica 7 — Libro del ritual y Veleth  ← el hueco más grande

### Rediseñar la mecánica del libro al modelo documentado
`RitualBookDirector` implementa hoy una mecánica distinta: una "apertura" continua que
se cierra sola con el tiempo y se abre alumbrando. El doc pide un **evento discreto de
oscuridad** que aparece en un intervalo aleatorio de 30-50 s. — *US-7.2, prioridad Alta*

### Disipar la oscuridad alumbrando 4 segundos
Apuntar la linterna al libro mientras está siendo consumido debe disipar la oscuridad
a los 4 s de contacto continuo con la luz. — *US-7.3, prioridad Alta*

### Perder el libro si no se defiende en 6 segundos
Ventana de 6 s desde que aparece la oscuridad; si no se defiende, el libro se consume
y se pierde de forma definitiva (no se recupera). — *US-7.4, prioridad Alta*

### Sonido de alerta que invoca a Veleth
Al perder el libro debe dispararse un sonido de alerta exclusivo que funciona como la
convocatoria de Veleth. — *US-7.5, prioridad Alta*

### Entidad Veleth
No existe en el código. Hoy `RitualBookDirector.CerrarYMatar()` mata a todos los
jugadores directamente (el propio comentario dice "por ahora no hay otra consecuencia").
Falta: modelo/apariencia, IA de persecución activa sin posibilidad de escape, y muerte
al alcanzar al jugador. — *US-7.6, prioridad Alta, 20 SP*

---

## Épica 9 — Multijugador y escalado

### Sistema de ping con linterna + botón secundario
Apuntar la linterna a un punto y pulsar el botón secundario del joystick emite una
señal visible y audible para todos los jugadores. Es el canal de coordinación de
respaldo si el audio de voz falla. — *US-9.7, prioridad Baja*

### Probar el chat de voz en dispositivos reales
El chat de voz ya está implementado (`Assets/Voice/`) y compila, pero **no se probó en
device**. Validar: latencia real, si el jitter buffer de 90 ms alcanza, sensibilidad
por defecto de la VAD con ruido de fondo, y permisos de micrófono en Android e iOS.

---

## Épica 10 — Infraestructura técnica

### Opciones de confort
`GameOptions` sólo persiste volumen maestro, anclas y los ajustes de voz. Faltan las
opciones del doc: brillo, reducción de aberración, reducir efectos y ajuste de motion
sickness. — *US-10.2, prioridad Baja*

---

## Épica 11 — Efectos visuales y sonoros

### Filtro VHS / cámara antigua
Efecto a pantalla completa sobre toda la imagen. Sin rastros en el código.
— *US-11.1, prioridad Media*

### Aberración cromática y distorsión de lente globales
Efecto de pantalla completa en momentos de tensión. Ojo: existe la distorsión
*localizada* de Arbmos (`ArbmosDistortionHUD`), que es un efecto distinto y acotado a
esa entidad — no cubre esta historia. — *US-11.2, prioridad Media*

### Banda sonora ambiental reactiva
Música adaptativa según el evento y el nivel de peligro. Hoy sólo hay audio puntual por
evento. — *US-11.3, prioridad Media*
