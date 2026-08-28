# Sorken — inventario y condiciones de animación

Estado: borrador funcional. Condiciones actualizadas segun definicion de gameplay. Las asociaciones marcadas **por confirmar** deben validarse reproduciendo el clip completo antes de integrarlas al Animator.

## Prioridad general

De mayor a menor prioridad:

1. Ataque de agarre.
2. Emergencia desde oscuridad.
3. Reacción a la linterna.
4. Persecución cercana.
5. Idle.

Una acción de prioridad alta bloquea las inferiores hasta terminar, salvo la reacción a la linterna durante locomoción, que puede encadenar una entrada y luego una variante específica de caminata.

## Clips encontrados

### 1. Idle — `01a03225...` — 4 s

- Visual: postura erguida con oscilación corporal leve.
- Tipo: bucle.
- Entra cuando: Sorken está activo, apoyado y su velocidad horizontal es casi cero.
- Sale cuando: comienza a desplazarse, emerge, reacciona a la linterna o ataca.
- No debe mover el objeto raíz por el escenario.

### 2. Walking — `Animation_Walking` — 1,067 s

- Visual: caminata humanoide normal.
- Tipo: bucle.
- Estado: no se usa en el flujo actual.
- Motivo: no forma parte del comportamiento final definido para Sorken.
- Se conserva solo como referencia o descarte futuro.

### 3. Running — `Animation_Running` — 0,667 s

- Visual: carrera rápida.
- Tipo: bucle.
- Estado: no se usa en el flujo actual.
- Motivo: no transmite el peso buscado para Sorken.
- Se conserva solo como referencia o descarte futuro.

### 4. Injured Walk — `Animation_Injured_Walk` — 8,7 s

- Visual: caminata herida.
- Tipo: bucle.
- Estado: caminata principal en uso.
- Entra cuando: Sorken se desplaza sin estar recibiendo luz directa de la linterna y sin ejecutar emergencia ni ataque.
- Permanece mientras: sigue persiguiendo o avanzando en estado normal.
- Sale cuando: entra en `Inicio de cubrirse`, `Caminar cubriéndose`, una emergencia o el ataque de agarre.
- Nota: aunque el nombre del clip sugiera daño, visualmente queda definido como la caminata base actual del personaje.

### 5. Inicio de cubrirse el rostro — `01a03212...` — 3 s

- Visual: parte erguido y eleva/cruza los brazos delante de la cara.
- Tipo: acción de entrada, no bucle.
- Entra cuando: la linterna lo apunta directamente con línea de visión.
- Frecuencia: solo una vez por cada inicio real de exposición a la linterna.
- Durante el clip: no debe desplazarse; queda quieto mientras se cubre.
- Sale hacia: `Caminar cubriéndose` si la linterna sigue apuntándolo y retoma persecución.
- Si el jugador deja de apuntarlo: no vuelve a dispararse hasta una nueva exposición clara.

### 6. Postura cubierta / persecución cercana — `01a03217...` — 3 s — **por confirmar**

- Visual: brazos elevados y cruzados frente a la parte superior del cuerpo; hay movimiento leve de piernas.
- Estado: en desuso por ahora.
- No forma parte del flujo actual.
- Se conserva solo para reevaluacion futura si hace falta una variante de persecucion cercana o una transicion extra.

### 7. Emergencia por ventana — `01a03222...` — 4,5 s — **por confirmar**

- Visual: inclina el cuerpo, alterna apoyos de brazos y vuelve erguido al final.
- Tipo: acción única.
- Entra cuando: el evento de aparición selecciona una ventana válida.
- Previo al clip: aparece oscuridad vertical en la abertura y permanece 5 s antes de comenzar la animación.
- Durante el clip: navegación, giro automático y persecución quedan bloqueados; el movimiento debe seguir la abertura, no subir desde el suelo.
- Sale cuando: alcanza la pose erguida dentro de la habitación; entonces habilita navegación y pasa a Idle o persecución.
- Solo puede reproducirse una vez por aparición.

### 8. Ataque de agarre — `01a03224...` — 2 s — **por confirmar**

- Visual: prepara las manos, comprime el torso y cierra ambos brazos.
- Tipo: acción única.
- Entra cuando: jugador a distancia de captura, dentro del ángulo frontal, con línea de visión y sin obstáculos.
- Durante el clip: detiene la navegación y orienta al Sorken hacia el jugador antes de comenzar; después no corrige el giro bruscamente.
- El agarre se confirma mediante un evento en el cuadro de contacto, no al comenzar la animación.
- Si el jugador abandona el volumen antes del evento de contacto, el ataque falla y pasa a recuperación/persecución.

### 9. Emergencia por puerta — `01a043d5...` — 3 s

- Visual: atraviesa agachado una abertura vertical y termina erguido.
- Tipo: acción única.
- Entra cuando: el evento de aparición selecciona una puerta válida.
- Previo al clip: aparece oscuridad vertical en la abertura y permanece 5 s antes de comenzar la animación.
- Durante el clip: la puerta física no necesita abrirse; navegación y giro automático quedan bloqueados.
- Sale cuando: queda erguido dentro de la habitación; se disipa la oscuridad por detrás y comienza Idle o persecución.
- Solo puede reproducirse una vez por aparición.

### 10. Cubrirse mientras avanza — `01a043da...` — 4 s

- Visual: mantiene ambas manos sobre la cara mientras camina.
- Tipo: bucle, sujeto a comprobar continuidad del primer y último cuadro.
- Entra cuando: termina `Inicio de cubrirse` y la linterna sigue apuntándolo.
- Permanece mientras: continúa la persecución y el haz sigue sobre la cabeza o torso superior.
- Durante el clip: avanza un poco más lento que en persecución normal.
- Sale inmediatamente con una transición corta cuando: el haz deja de tocarlo, la linterna se apaga o se rompe la línea de visión.
- Nunca se activa solo por estar cerca del jugador.

## Comportamiento todavía sin clip confirmado

### Brazos extendidos a corta distancia

- Entra cuando: está persiguiendo, el jugador se encuentra a 3 m o menos y no se está ejecutando ataque, emergencia ni reacción a la linterna.
- Permanece mientras: la distancia sea igual o inferior a 3 m.
- Sale cuando: la distancia supera 3 m, con histéresis propuesta de salida a 3,3 m para evitar cambios constantes.
- Prioridad: la reacción a la linterna gana y obliga a cubrir la cara; el ataque de agarre gana sobre ambas.
- Falta confirmar si `01a03217...` representa este movimiento o si se necesita otro clip.

## Datos técnicos comunes

- Todos los clips se detectaron a 30 FPS y con esqueletos de 24 huesos.
- Los FBX con UUID contienen una toma auxiliar de dos cuadros y una toma principal. En Unity debe seleccionarse únicamente la toma principal.
- La orientación, avatar, root motion, continuidad del bucle y texturas se validarán antes de construir el Animator.

## Tabla operativa

| Estado | Clip | Tipo | Entra cuando | Sale cuando | Prioridad |
| --- | --- | --- | --- | --- | --- |
| Idle | `01a03225...` | Bucle | Esta activo, erguido y sin desplazarse | Empieza a moverse, emerge, recibe linterna o ataca | 5 |
| Caminata base | `Animation_Injured_Walk` | Bucle | Se desplaza en estado normal, sin luz directa de linterna | Recibe linterna, emerge, ataca o se detiene | 4 |
| Inicio de cubrirse | `01a03212...` | Unica | La linterna lo apunta directamente con linea de vision | Termina el clip; si la luz sigue, pasa a `Caminar cubriendose`; si no, vuelve a locomocion o idle | 2 |
| Caminar cubriendose | `01a043da...` | Bucle | Termina `Inicio de cubrirse` y la linterna sigue apuntandolo | La luz deja de apuntarlo, se rompe la vision, emerge, ataca o se detiene | 3 |
| Emerger por ventana | `01a03222...` | Unica | Se elige una ventana valida, aparece oscuridad vertical, esperan 5 s y arranca el clip | Termina erguido dentro de la habitacion | 1 |
| Emerger por puerta | `01a043d5...` | Unica | Se elige una puerta valida, aparece oscuridad vertical, esperan 5 s y arranca el clip | Termina erguido dentro de la habitacion | 1 |
| Ataque de agarre | `01a03224...` | Unica | El jugador entra al rango de agarre con vision directa y angulo valido | Termina el clip o falla el contacto | 0 |

## Notas de implementacion

- `Walking`, `Running` y `Postura cubierta / persecucion cercana` quedan fuera del flujo actual.
- `Inicio de cubrirse` no debe mover al personaje; solo prepara la transicion visual.
- `Caminar cubriendose` debe usar una velocidad mas lenta que la caminata base.
- Las emergencias bloquean navegacion y giro automatico hasta terminar.
- El ataque de agarre bloquea cualquier otro estado mientras se ejecuta.
