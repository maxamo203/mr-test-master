# Reglas de sonidos ambientales

## Golpes de puerta

Clips:
- `Assets/Audio/Clips/Ambiente/golpes_puerta_01.wav`
- `Assets/Audio/Clips/Ambiente/golpes_puerta_02.wav`

Reglas:
- Evento aleatorio de tensión, entre 1 y 2 veces por noche.
- Solo puede ocurrir después de 5 a 10 segundos continuos sin eventos.
- Cada clip es una variante independiente: reproducir uno u otro, nunca ambos juntos.

## Manija de puerta

Clip: `Assets/Audio/Clips/Ambiente/manija_puerta_01.wav`

Reglas:
- Evento aleatorio de tensión con la misma condición de inactividad de 5 a 10 segundos.
- Es un único sonido; se reproduce de forma independiente a los golpes de puerta.

## Entrada de Sorken por puerta

Clip: `Assets/Audio/Clips/Sorken/entrada_puerta_crujido.wav`

Regla:
- Reproducir cuando Sorken comienza a entrar por una puerta.

## Pisadas de Sorken

Clips: `Assets/Audio/Clips/Sorken/paso_entrada_puerta_01.wav` a `paso_entrada_puerta_04.wav`.

Reglas:
- Cada archivo contiene una sola pisada.
- Sincronizar cada clip con el apoyo correspondiente de Sorken.
- No reproducir las cuatro pisadas como un único audio.
