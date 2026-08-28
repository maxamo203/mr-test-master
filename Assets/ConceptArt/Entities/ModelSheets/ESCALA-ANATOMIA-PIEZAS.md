# Escala, anatomía y piezas de las entidades

Alcance de esta ficha: escala objetivo, estructura anatómica y separación lógica de piezas. No define todavía topología, UV, materiales, rig ni animaciones.

## Convenciones comunes

- Escala de Unity: **1 unidad = 1 metro**.
- Origen del modelo: centro del apoyo principal sobre el suelo (`Y = 0`).
- Eje frontal: `+Z`; lateral derecho del personaje: `+X`; vertical: `+Y`.
- Las medidas son objetivos iniciales. Deben validarse a escala 1:1 en AR antes de cerrar el modelo.
- Alas plegadas por defecto. Su silueta no debe superar los límites indicados.
- Halo, ojos, reliquarios y elementos rígidos deben poder ocultarse o reemplazarse por separado.

## Veleth

### Escala objetivo

| Medida | Valor |
|---|---:|
| Altura total, incluyendo corona | 2,00 m |
| Altura del cuerpo hasta la cabeza | 1,82 m |
| Ancho máximo de hombros | 0,42 m |
| Ancho máximo con alas plegadas | 0,68 m |
| Profundidad corporal | 0,26 m |
| Diámetro de la cabeza blanca | 0,34 m |
| Diámetro exterior del halo | 0,52 m |
| Altura del borde inferior del cuerpo | 0,00 m |

### Anatomía estructural

- Tronco humanoide extremadamente estrecho, sin volumen muscular visible.
- Un par de brazos largos con cinco dedos cada uno.
- Cuello fino que sostiene una cabeza ovoide sin rostro.
- Parte inferior continua, semejante a hábito o columna de sombra; internamente puede esconder dos piernas simples para locomoción.
- Un par de alas pequeñas y dañadas, plegadas desde la zona escapular hacia abajo.
- Centro visual rígido: cabeza blanca, malla dorada, halo y reliquario de garganta.

### Piezas separadas

1. `Veleth_Body`: torso, cuello, falda de sombra y piernas internas.
2. `Veleth_Arm_L` y `Veleth_Arm_R`: brazos y manos.
3. `Veleth_HeadWhite`: volumen blanco estratificado, completamente sin facciones.
4. `Veleth_GoldMesh`: cubierta dorada independiente sobre la cabeza.
5. `Veleth_Halo`: aro gótico rígido.
6. `Veleth_CrownRays`: siete rayos; pueden agruparse en una sola pieza rígida.
7. `Veleth_ThroatReliquary`: pieza rígida central.
8. `Veleth_Wing_L` y `Veleth_Wing_R`: alas plegadas independientes.
9. `Veleth_ShadowTrails`: faldones y filamentos secundarios separados del cuerpo principal.

## Arbmos

### Escala objetivo

| Medida | Valor |
|---|---:|
| Altura total, incluyendo alas plegadas | 2,00 m |
| Ancho del torso cerrado | 0,68 m |
| Ancho máximo con brazos de apoyo | 1,18 m |
| Ancho máximo de alas plegadas | 0,90 m |
| Profundidad máxima | 0,46 m |
| Altura de la cavidad ocular | 0,86 m |
| Profundidad de la cavidad | 0,22 m |
| Huella de la base | 0,68 × 0,54 m |

### Anatomía estructural

- Núcleo vertical sin cabeza convencional.
- Dos caparazones costales forman las puertas del torso-relicario.
- Cavidad frontal profunda con un vacío circular central.
- Tres pares de brazos pequeños sujetan y abren los caparazones.
- Un par de brazos primarios largos funciona como apoyo y locomoción.
- Columna dorsal continua y base de raíces que aporta el tercer apoyo corporal.
- Un par de alas corruptas compactas, plegadas junto a la espalda.
- Conjunto ocular frontal compuesto por ojos de distintos tamaños; todos pertenecen a la cavidad, no a los caparazones externos.

### Piezas separadas

1. `Arbmos_Core`: columna, espalda y masa central.
2. `Arbmos_RibShell_L` y `Arbmos_RibShell_R`: puertas costales móviles.
3. `Arbmos_Cavity`: revestimiento interior negro.
4. `Arbmos_CentralVoid`: disco o túnel central independiente.
5. `Arbmos_Eyes`: conjunto de ojos; usar varios tamaños reutilizables manteniendo cada globo orientable.
6. `Arbmos_GrasperA_L/R`, `GrasperB_L/R`, `GrasperC_L/R`: tres pares de brazos pequeños.
7. `Arbmos_SupportArm_L` y `Arbmos_SupportArm_R`: brazos largos de apoyo.
8. `Arbmos_RootSkirt`: base de raíces y filamentos.
9. `Arbmos_Wing_L` y `Arbmos_Wing_R`: alas plegadas independientes.

## Sorken

### Escala objetivo

| Medida | Valor |
|---|---:|
| Altura total completamente erguido | 2,20 m |
| Altura habitual en postura quebrada | 2,05 m |
| Altura hasta hombros, erguido | 1,68 m |
| Ancho de hombros | 0,48 m |
| Ancho máximo con alas plegadas | 0,62 m |
| Profundidad del torso | 0,32 m |
| Longitud de mano | 0,34 m |
| Longitud de pie | 0,36 m |
| Diámetro exterior del halo | 0,44 m |

### Anatomía estructural

- Humanoide bípedo extremadamente alargado.
- Un par de brazos y un par de piernas; cinco dedos largos por mano y cinco dedos de apoyo por pie.
- Columna con curva torácica marcada y cuello proyectado hacia delante.
- Hombros estrechos, caja torácica abierta con forma de catedral y pelvis compacta.
- Codos, muñecas, rodillas y tobillos conservan articulaciones reconocibles, pero con rangos visualmente quebrados.
- Máscara funeraria fusionada al cráneo; ojos emisivos debajo de la máscara.
- Cabello largo que cubre parcialmente cuello, espalda y raíces de alas.
- Un par de alas vestigiales reducidas a varillas óseas y tiras oscuras pegadas a la espalda.

### Piezas separadas

1. `Sorken_Body`: tronco, pelvis, brazos y piernas como anatomía principal.
2. `Sorken_Hands`: manos separadas durante escultura para controlar dedos largos.
3. `Sorken_Feet`: pies separados durante escultura para controlar apoyo y garras.
4. `Sorken_RibCage`: estructura ósea exterior del pecho.
5. `Sorken_FaceMask`: máscara funeraria rígida.
6. `Sorken_Eyes`: dos globos o planos emisivos independientes.
7. `Sorken_Hair`: masa principal y mechones secundarios.
8. `Sorken_Halo`: aro vertebral rígido.
9. `Sorken_Wing_L` y `Sorken_Wing_R`: alas vestigiales plegadas.
10. `Sorken_BackStrands`: tiras oscuras secundarias que rompen la silueta posterior.

## Criterio de aprobación de estos dos pasos

- Las vistas frontal, lateral y posterior representan la misma anatomía sin cambios de escala o número de piezas.
- Cada entidad conserva una silueta inequívoca sin depender de alas abiertas.
- Las dimensiones caben en un interior doméstico y pueden probarse a escala 1:1 en Unity.
- Toda pieza con material, movimiento o visibilidad independiente está identificada por separado.
