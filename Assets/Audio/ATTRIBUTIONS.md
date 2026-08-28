# Atribuciones de audio — Mortuorium

Todos los clips de `Assets/Audio/Clips/` son **placeholder**: sirven para que el sistema
de audio funcione y se pueda calibrar la mezcla, y están pensados para reemplazarse por
audio propio o definitivo más adelante.

Fuente de todos: [OpenGameArt.org](https://opengameart.org).

---

## ⚠️ OBLIGATORIO — hay que acreditar esto en el juego

**Horror Sound Effects Library** — Little Robot Sound Factory
[opengameart.org/content/horror-sound-effects-library](https://opengameart.org/content/horror-sound-effects-library)
Licencia: **CC-BY 3.0** (Creative Commons Attribution)

> El autor pide: *"Attribute Little Robot Sound Factory, and provide this link where
> possible: www.littlerobotsoundfactory.com"*

**Texto sugerido para la pantalla de créditos:**

```
Efectos de sonido: Little Robot Sound Factory
www.littlerobotsoundfactory.com  (CC-BY 3.0)
```

Mientras esos clips estén en el juego, **esta línea tiene que aparecer en los créditos**.
Si se reemplazan todos por audio propio, la obligación desaparece.

Clips usados de este pack (34):
`Musica/ambiente_noche`, `Musica/victoria_amanecer`, `Musica/derrota_muerte`,
`Sorken/entrada_puerta_01`, `Sorken/entrada_ventana_01`, `Sorken/entrada_ventana_02`,
`Sorken/entrada_generica_01`, `Sorken/entrada_generica_02`, `Sorken/hueso_01..04`,
`Sorken/entra`, `Sorken/repelido`, `Sorken/grab`, `Sorken/apuntado`,
`Arbmos/drena_loop`, `Arbmos/susurros_loop`, `Arbmos/gritos_loop`,
`Arbmos/embestida`, `Arbmos/jumpscare`,
`Veleth/invocacion_01`, `Veleth/persecucion_loop`, `Veleth/grab`,
`Libro/defendiendo_loop`, `Libro/perdido`,
`Cordura/cordura_baja`, `Cordura/cordura_cero`, `Cordura/noche_desbloqueada`

---

## Sin obligaciones — CC0 (dominio público)

Estos no exigen nada. El crédito es opcional y sólo por cortesía.

**RPG Sound Pack** — artisticdude
[opengameart.org/content/rpg-sound-pack](https://opengameart.org/content/rpg-sound-pack) — **CC0**
Usado en: `Sorken/entrada_puerta_02`, `Arbmos/aparece`, `Veleth/invocacion_02`,
`Libro/ataque_empieza`, `Libro/salvado`, toda la carpeta `Linterna/`,
`Cordura/reloj_final`, toda la carpeta `UI/`.

**Ambience Pack 1 – Sci Fi Horror** — Joth
[opengameart.org/content/ambience-pack-1-sci-fi-horror](https://opengameart.org/content/ambience-pack-1-sci-fi-horror) — **CC0**
Usado en: `Musica/musica_menu`, `Musica/capa_tension`, `Musica/musica_persecucion`.

**Footsteps** — GboxMikeFozzy
[opengameart.org/content/footsteps-0](https://opengameart.org/content/footsteps-0) — **CC0**
Usado en: `Sorken/paso_01..06`.

---

## Notas técnicas

- Se usaron los **MP3** del pack de terror, no los WAV: 7,4 MB contra 68 MB, y Unity
  recomprime a Vorbis al importar igual. Para placeholder no justifica el peso en LFS.
- Los `.wav/.mp3/.ogg` van a **Git LFS** por `.gitattributes`. Después del primer push:
  `git lfs push --all origin`, o en las otras máquinas quedan punteros y el juego sale
  mudo sin ningún error visible.
- Los clips 3D se importan con `forceToMono`: un clip estéreo en una fuente 3D ensucia la
  localización, y mono además ocupa la mitad.
