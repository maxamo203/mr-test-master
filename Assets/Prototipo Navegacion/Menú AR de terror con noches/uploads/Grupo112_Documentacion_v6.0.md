n

# Universidad Nacional de La Matanza

Cátedra de Proyecto Final

**Año 2026**

**Proyecto:**

**“Mortuorium”**

**Número de Equipo: 112**

**Integrantes del Equipo de Proyecto:**

|     |     |     |
| --- | --- | --- |
| DNI | Nombre | E-Mail |
| 44689133 | BOSCH MAXIMO AUGUSTO | mbosch@alumno.unlam.edu.ar |
| 38695645 | DI TOMMASO GIULIANO | gditommaso@alumno.unlam.edu.ar |
| 40015557 | RIOS CRISTIAN MARCELO | crrios@alumno.unlam.edu.ar |
| 43386520 | VALLEJOS FRANCO NICOLAS | franvallejos@alumno.unlam.edu.ar |
|     |     |     |

**Profesores:**

**Jefe de Cátedra:** Mg. Roberto Eribe

**Profesor a cargo del curso**: Ing. Mariano Bucher

**Auxiliar a cargo del proyecto:** _Ing. Camila Mancusi, Ing. Ariel Molina_

**ÍNDICE**

[1\. Resumen preliminar 2](#_Toc941822163)

[1.1 Objetivos del proyecto 3](#_Toc1046369156)

[1.2 Breve descripción del proyecto 4](#_Toc554262437)

[Límites 7](#_Toc2025132745)

[Incluido 7](#_Toc899345292)

[No incluido 7](#_Toc505471090)

[1.3 Beneficios al Negocio 7](#_Toc1866908989)

[1.4 Plan a Alto Nivel 8](#_Toc399862020)

[1.5 Riesgos Identificados 8](#_Toc1542272611)

[2\. Modelo de Negocio 9](#_Toc1019371609)

[2.1 Business Model Canvas 9](#_Toc941169557)

[2.2 Explicación de los nueve módulos 9](#_Toc1560541921)

[2.3 Oferta - Cuadro de competidores 10](#_Toc2112961536)

[2.4 Oferta - Productos complementarios 10](#_Toc556403379)

[2.5 Análisis económico-financiero 10](#_Toc637811054)

[3\. Definición de alcance 11](#_Toc1841379586)

[3.1 Visual story Mapping 12](#_Toc1079114031)

[3.2 Product Backlog 12](#_Toc999425594)

[3.3 Criterios de aceptación 12](#_Toc370663199)

[3.4 Criterios de completado. 13](#_Toc2142391130)

[4\. Equipo e Interesados 14](#_Toc461096576)

[4.1 Equipo de proyecto 14](#_Toc1658403993)

[4.2 Matriz de interesados 14](#_Toc526697196)

[5\. Release planning - Plan de versiones 15](#_Toc1401451598)

[5.1 Estimación Story Points 15](#_Toc1440482811)

[5.2 Plan de versiones 15](#_Toc700309357)

[6\. Plan de comunicaciones 16](#_Toc77412349)

[6.1 Comunicaciones del proyecto 16](#_Toc1627255769)

[7\. Experiencia de Usuario 16](#_Toc1112880779)

[7.1 Prototipo de navegación 17](#_Toc651782052)

[7.2 Wireframes Mockups 17](#_Toc572984321)

[8\. Arquitectura de Software 17](#_Toc679772363)

[8.1 Diagrama de Arquitectura 18](#_Toc503571019)

[8.2 Frameworks / Componentes utilizados 18](#_Toc496345493)

[8.3 Infraestructura tecnológica 18](#_Toc60367470)

[8.4 Otros diagramas según corresponda 19](#_Toc130614882)

[8.5 Estándar de codificación 19](#_Toc1534497357)

[9\. Plan de pruebas 19](#_Toc138353125)

[9.1 Diseño y ejecución de escenario de prueba 20](#_Toc1543971994)

[9.2 Seguimiento de fallas 20](#_Toc695443814)

[10\. Manual Instalación 20](#_Toc1325841334)

[11\. Hoja de Ruta 21](#_Toc653049391)

[Anexos I. Retrospectiva 23](#_Toc547280451)

[Anexos II. Riesgos 24](#_Toc401140104)

[Anexos III. Minuta de reunión 25](#_Toc259124805)

[Anexos IV. Paper investigación 26](#_Toc2085453058)

# 1\. Resumen preliminar

1.0

&lt;Bosch, Di Tommaso, Rios, Vallejos&gt;

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
| 2026-05-07 | 1.0 | Resumen preliminar inicial | Bosch, Di Tommaso, Rios, Vallejos |
|     |     |     |     |

## 1.1 Objetivos del proyecto

- Diseñar y desarrollar un videojuego de terror funcional y jugable para dispositivos móviles con soporte VR (cardboard), capaz de sostener sesiones de entre 5 y 10 minutos por noche, en el que el jugador deba gestionar recursos limitados, mantener su cordura, mantener el libro del ritual que salió mal y superar 6 noches de dificultad creciente, enfrentando entidades con comportamientos distintos y mecánicas propias.
- Reconocer y modelar el entorno del jugador para determinar los puntos de spawn mediante las capacidades de mapeo espacial del dispositivo móvil.
- Un sistema donde el libro del ritual es consumido por la oscuridad cada cierto tiempo y el jugador deba apuntar con la linterna sobre el mismo para salvarlo.
- Implementar un sistema de dificultad progresivo que escale la frecuencia de aparición de entidades, el consumo de batería y el deterioro de la cordura a medida que avanzan las noches.
- Desarrollar el comportamiento individual de cada entidad (Sorken, Arbmos y Veleth), garantizando que cada una presente un desafío diferenciado y reconocible para el jugador
- Diseñar un sistema de efectos visuales y sonoros (VHS, aberración cromática, distorsión de lente, lens flare) que refuerce la atmósfera y la sensación de tensión constante.
- Integrar un joystick externo vinculado por Bluetooth al dispositivo móvil, cuyo botón principal controla el encendido y apagado de la linterna como mecánica central de interacción.

## 1.2 Breve descripción del proyecto

Alcance:

**Plataforma y Hardware**

El juego se desarrolla para smartphones utilizados dentro de un soporte VR tipo cardboard o equivalente de bajo costo. La visualización es estereoscópica con seguimiento de orientación por giroscopio e IMU del dispositivo.

El joystick externo se conecta al teléfono vía Bluetooth y su función principal es el control de la linterna:

- Botón principal: encender / apagar la linterna
- Botón secundario (opcional): Confirmación de acciones
- La dirección de la linterna está dada por la orientación del celular en la visión del usuario.

**Sistema de Noches y Progresión**

- El juego comprende un total de 6 noches jugables, cada una más difícil que la anterior.
- Cada sesión de juego nocturna tiene una duración objetivo de entre 5 y 10 minutos.
- El progreso se guarda entre noches, permitiendo al jugador retomar la partida desde la noche en que se encuentre sin perder el avance.
- La dificultad escala en los siguientes parámetros por noche:
- Mayor frecuencia de aparición de entidades.
- Menor duración de la batería de la linterna.
- Mayor consumo de cordura por acción u omisión.
- Mayor frecuencia de aparición de oscuridad en el libro del ritual

**Entorno del Juego — Escaneo del Espacio Físico**

El juego se desarrolla en el entorno físico real del jugador. Antes de iniciar la primera noche, el sistema solicita al jugador que escanee su espacio mediante las capacidades de mapeo espacial del dispositivo móvil.

- El escaneo previo es obligatorio para poder iniciar el juego.
- A partir del mapa del espacio físico relevado, el sistema determina de forma dinámica los puntos de spawn de objetos (baterías).
- Todos los puntos de modelado, del entorno escaneado, tienen como referencia, del centro del eje, la imagen escaneada principal.
- Esto garantiza que cada partida se adapte al espacio real del jugador, generando una experiencia personalizada e irrepetible.

**Sistema de Linterna y Baterías**

- La linterna cuenta con una batería limitada que se agota con el uso continuo.
- El jugador debe localizar baterías de repuesto dispersas por el entorno.
- Las baterías respetan las siguientes reglas de reaparición:
- No vuelven a aparecer en el mismo punto dentro de un período de 1 minuto.
- El temporizador de reaparición se activa únicamente cuando el jugador se aleja del punto de spawn correspondiente.
- Si el jugador permanece cerca del punto, la batería no reaparece.

**Sistema de Cordura**

- La cordura regula las acciones disponibles para el jugador y su deterioro genera efectos progresivos.
- Causas de pérdida de cordura:
- Evento aparición de Arbmos: invoca a Arbmos como alucinación por cierto tiempo, que drena la cordura continuamente si no te quedas quieto.
- Al llegar la cordura a cero, la interfaz del jugador se distorsiona visualmente indicando el estado crítico. En este estado el jugador no muere de forma inmediata, pero si se desencadena nuevamente una acción que invocaría a Arbmos (como quedarse quieto), este aparece como amenaza letal y provoca la muerte.
- El nivel de cordura no se puede recuperar, como forma de penalizar al jugador por moverse en los eventos de aparición del Arbmos.

Sistema de oscuridad en el libro del ritual

- El libro del ritual es un objeto que debemos mantener a salvo para sobrevivir.
- Siempre se genera sobre la imagen escaneada principal.
- Cada cierto tiempo, aparecerá una oscuridad obligando al jugador a alumbrar el mismo para salvarlo. Si el jugador no salva el libro, aparecerá la entidad veleth para perseguirlo sin posibilidades de escapar.
- El libro de ritual, una vez perdido, no se puede recuperar.

**Entidades**  
Las entidades son los enemigos principales del juego, los cuales el jugador debe evitar, ahuyentar o huir. Cuando una entidad se acerca a menos de un umbral de distancia del jugador, automáticamente este pierde (con una animación dependiendo del tipo de entidad).  
El juego cuenta con tres entidades con comportamientos, apariencias y mecánicas diferenciadas:

**Sorken — Entidad Principal**

- Intenta ingresar a la casa a través de puntos de entrada específicos (ventanas, armarios, ductos).
- Emite ruidos característicos por cada punto de entrada desde el que intenta ingresar.
- El jugador debe localizar el ruido y alumbrar a Sorken con la linterna durante varios segundos para ahuyentarlo.
- Si el jugador demora demasiado, Sorken entra y atrapa al jugador.
- Características visuales y sonoras: movimiento quebrado y errático, ruido de huesos rotos al desplazarse, pasos cuyo volumen aumenta exponencialmente al acercarse.
- Si Sorken inicia el ataque a distancia considerable, al aproximarse al jugador se activa una secuencia especial: la linterna no puede encenderse, los pasos dejan de escucharse y la visibilidad se reduce casi a cero.
- Una vez que la entidad entra en la habitación, puede ser ahuyentada si es iluminada (sin cortes) usando el 70% de la batería, acción que hace que se desplace más lento, mientras esta persigue al jugador.

**Arbmos — Entidad Secundaria (Sistema de Cordura)**

- Solo aparece en noches avanzadas (noche 4 en adelante).
- Aparece como alucinación a cada jugador de forma aleatoria durante cierto tiempo, solo afecta al jugador que lo ve.
- Si aparece, drenara cierto porcentaje de cordura al jugador si este se mueve.
- Tener la linterna apagada aumenta su probabilidad de aparición.
- Se convierte en amenaza letal únicamente cuando la cordura ya está en cero y el jugador vuelve a desencadenar una acción que lo invoca.
- Apariencia: figura oscura y semitransparente, aura de humo negro, ojos blancos brillantes con iluminación real en el entorno.
- Su presencia genera una distorsión de lente localizada (pixelado, rayado o desenfoque) en el segmento de pantalla donde aparece.
- Secuencia cuando es invocado con cordura en cero:
- Arbmos aparece cerca del jugador y permanece inmóvil unos segundos antes de atacar.
- El aura de humo no se muestra en esta fase.
- Al aparecer, se produce una distorsión brusca y momentánea de cámara.
- Los efectos escalan gradualmente: distorsión de lente progresiva, ruido de estática en aumento, susurros, gritos y sonidos guturales en escalada.
- Al alcanzar la intensidad máxima: jumpscare en primer plano.

**Veleth — Entidad de oscuridad del libro**

- Se invoca únicamente como consecuencia al fallar en la misión de proteger el libro.
- Su convocación se produce mediante el sonido de alerta que se activa al desaparecer el libro.
- Una vez invocada, Veleth persigue activamente al jugador hasta atraparlo.
- Comportamiento y apariencia a definir durante el desarrollo.

**Sistema de Efectos Visuales y Sonoros**

- Efectos visuales confirmados: filtro de VHS o cámara antigua, aberración cromática.
- Efectos adicionales: distorsión de lente, lens flare, distorsión de caras de personas cercanas.
- Diseño de audio: ruido único y reconocible por cada punto de entrada de Sorken, sonido de alerta exclusivo del sistema del libro del ritual, banda sonora ambiental que sostiene la tensión del entorno.

**Sistema Multijugador Local/Online (1 a 4 jugadores)**

El juego soporta sesiones multijugador de entre 1 y 4 jugadores. Cada jugador utiliza su propio smartphone con visor VR y joystick Bluetooth. Los jugadores comparten una misma sesión de juego nocturna y deben colaborar para sobrevivir la noche.

**Escalado de dificultad en multijugador:**

La dificultad escala automáticamente según la cantidad de jugadores activos en sesión:

|     |     |     |     |
| --- | --- | --- | --- |
| **Jugadores** | **Puntos de entrada de Sorken** | **Frecuencia de oscuridad en el libro** | **Consumo de cordura** |
| 1   | 1–2 simultáneos | Base | Base |
| 2   | 2–3 simultáneos | +20% | Base |
| 3   | 3–4 simultáneos | +40% | +10% |
| 4   | 4–5 simultáneos | +60% | +20% |

## Límites

### Incluido

- El juego comprende 6 noches jugables, sin niveles adicionales en esta versión.
- Las entidades incluidas son Sorken, Arbmos y Veleth; no se contempla la adición de nuevas entidades en esta versión.
- El entorno del juego se limita a una única locación (la casa) con sus puntos de entrada definidos.
- El sistema de oscuridad que consume le libro del ritual.
- El prototipo inicial se desarrolla sin soporte de plataformas externas (sin integraciones con servicios de logros, leaderboards ni distribución en tiendas digitales en esta etapa).
- Los efectos visuales se implementan sobre el motor elegido sin dependencia de plugins externos no auditados.
- El audio se gestiona internamente; no se contempla integración con motores de audio adaptativos de terceros en la versión inicial.

### No incluido

- Localización a idiomas distintos del español en la versión inicial.
- Soporte para visores VR de gama alta (Quest, Pico) en esta versión
- Soporte para pisos desnivelados o con escalones. Se asume un piso plano horizontal.

## 1.3 Beneficios al Negocio

1.  **Accesibilidad masiva:** al desarrollar para smartphone con visor cardboard, el juego puede alcanzar a millones de usuarios sin requerir hardware especializado, ampliando el universo de jugadores potenciales.
2.  **Viralidad orgánica por la experiencia cooperativa:** Las reacciones grupales al jugar juntos generan contenido espontáneo para redes sociales, funcionando como canal de marketing orgánico.
3.  **Mayor retención y rejugabilidad:** la dimensión social del multijugador aumenta significativamente la retención respecto a una experiencia puramente individual. Los jugadores tienden a volver para completar noches con distintos compañeros.
4.  **Base tecnológica reutilizable:** los sistemas de entidades, cordura, linterna, libro de ritual y networking desarrollados son reutilizables y portables a futuros proyectos VR/XR móviles.
5.  **Distribución simplificada:** la distribución vía Google Play Store o APK directo es más accesible y menos restrictiva que canales de plataformas cerradas, acelerando el tiempo al mercado.
6.  **Generación de datos de UX grupales en VR móvil:** el proyecto permite relevar métricas reales de jugabilidad cooperativa, comunicación entre jugadores, fatiga visual y comportamiento de grupo en entornos inmersivos, datos de alto valor para iteraciones futuras.

## 1.4 Plan a Alto Nivel

**Hito 1 - Prototipo de plataforma y controles - Fecha estimada: 2/7** Configuración del entorno de desarrollo, integración del joystick Bluetooth, funcionamiento básico del head tracking, renderizado estereoscópico. Escaneo y modelación del entorno del usuario. Validación en dispositivo físico.

**Hito 2 - Prototipo jugable single player - Fecha estimada: 23/7** Mecánicas de linterna, baterías, cordura y Sorken funcionales. Sistema de sonido espacial integrado. Spawn procedural por zonas escaneadas. Desarrollo orientado a una arquitectura cliente-servidor para luego implementar un sistema multijugador.

**Hito 3 - Integración multijugador - Fecha estimada: 20/8** Sistema de sesiones de 1 a 4 jugadores, sincronización de entidades, chat de voz, escalado dinámico de dificultad y mecánica de estabilización entre jugadores funcionales. Verificación de latencia y sincronización.

**Hito 4 - Versión beta completa (6 noches)** **\- Fecha estimada: 10/9** Sistema de oscuridad que consume el libro, Veleth y Arbmos con cordura individual por jugador, escalado de dificultad por noche y cantidad de jugadores, guardado de progreso por perfil integrados.

**Hito 5 - Versión final - Fecha estimada: 1/10** Efectos visuales y sonoros refinados, equilibrio de dificultad validado en sesiones multijugador con testers, optimización de rendimiento para gama media y documento de diseño actualizado.

## 1.5 Riesgos Identificados

- Latencia de red en sesiones multijugador online: la sincronización en tiempo real de posiciones de entidades, estado de cordura y eventos de juego entre 2 a 4 dispositivos móviles es técnicamente exigente. Picos de latencia pueden desincronizar el estado del juego entre jugadores, generando situaciones injustas o confusas como que un jugador vea a Sorken en una posición diferente al resto.
    - Mitigacion: implementar un modelo de sincronización con tolerancia al lag (dead reckoning) que interpole las posiciones de las entidades localmente en cada dispositivo, reduciendo la dependencia de una conexión perfecta. Definir un umbral de latencia máxima aceptable durante las pruebas y ajustar la frecuencia de sincronización según el ancho de banda disponible.
- Latencia del joystick Bluetooth bajo carga de red simultánea: operar el joystick y la conexión de datos en simultáneo en el mismo dispositivo puede incrementar la latencia del input de la linterna, afectando la mecánica más crítica del juego.
    - Mitigacion: Realizar pruebas de latencia de input en condiciones de red activa desde etapas tempranas del desarrollo para detectar umbrales problemáticos. Ajustar periodos de reacción que debería tener el usuario para la utilización del joystick, para contemplar el posible retardo.
- Drift y re-localización del entorno según dispositivos: en algunos dispositivos, al localizar un objeto con realidad aumentada y ubicarlo en el entorno, para luego agitar el dispositivo o apuntar bruscamente a otra zona, la ubicación inicial en el espacio se puede perder. Al volver a localizar el objeto en su ubicación inicial con ayuda de alguna imagen escaneada principal, se vuelve a situar éste en el entorno. Este drift espacial se reduce mucho más en dispositivos con iOS que en Android.
    - Mitigación: Sugerir iOS como sistema operativo principal al usuario para conseguir la mejor experiencia, dejando Android como una posibilidad (no óptima). Requerir también, como parte de la jugabilidad, que cada cierto tiempo/actividades del gameplay se deba reapuntar a un QR localizado desde el inicio, el cual servirá como punto de referencia para ubicar el entorno. Esto se detalla en la sección de jugabilidad.
- Fatiga visual y motion sickness: el soporte VR móvil es más propenso a generar incomodidad que visores dedicados, especialmente si la cantidad de fotogramas por segundo cae por debajo de 60. En sesiones grupales esto puede hacer que algunos jugadores deban abandonar antes de completar la noche, afectando la experiencia del grupo.

Mitigacion: Optimizar el renderizado estereoscópico para mantenerlo en dispositivos de gama media. Incluir opciones de confort en el menú (reducción de campo visual, punto de referencia fijo en el HUD). Realizar sesiones de prueba con usuarios reales desde el primer prototipo para detectar y corregir fuentes de incomodidad antes de que se acumulen.

- Sobrecalentamiento del dispositivo: el renderizado estereoscópico continuo combinado con la conexión de red activa puede elevar la temperatura del dispositivo rápidamente, activando throttling térmico y provocando caídas de framerate que afectan la inmersión y pueden causar mareo.
    - Mitigacion: Diseñar las sesiones con la duración objetivo de 5 a 10 minutos precisamente para limitar la exposición térmica continua. Incluir advertencias al usuario si el dispositivo supera un umbral de temperatura durante la sesión.
- Coordinación de voz como factor externo no controlable: la efectividad de la comunicación entre jugadores depende en gran parte de la calidad del micrófono y auriculares de cada dispositivo. Una mala calidad de audio de voz en sesiones online puede eliminar la coordinación como herramienta, degradando la experiencia cooperativa a una individual desconectada.
    - Mitigacion: complementar el sistema de voz con un sistema de ping visual y sonoro que no dependa de la comunicación verbal: apuntar la linterna hacia un punto y presionar el botón secundario del joystick emite una señal visible y audible para todos los jugadores. Esto garantiza un canal de coordinación mínimo funcional independientemente de la calidad del audio de cada dispositivo.

# 2\. Modelo de Negocio

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 2.1 Business Model Canvas

https://canva.link/fopvvj3wn079g6w

https://www.strategyzer.com/canvas/business-model-canvas  

## 2.2 Explicación de los nueve módulos

2.2.1 Segmento de mercado

El producto apunta a cuatro segmentos principales. El primero y más amplio son gamers mobile de 14 a 30 años con interés en terror o experiencias VR que están dispuestos a explorar experiencias inmersivas de bajo costo.

El segundo son grupos de amigos que buscan entretenimiento en casa: el modo cooperativo hasta 4 jugadores convierte a Mortuorium en un producto de ocasión grupal para sumergirse en una experiencia de terror en tu propio ambiente.

El tercer segmento son los creadores de contenido y streamers en plataformas como TikTok, YouTube y Twitch: la reacción auténtica ante el terror en el espacio real del jugador genera contenido espontáneo de alta viralidad. Finalmente, los estudiantes universitarios son un segmento natural de adopción temprana, en el ecosistema de la UNLaM durante la fase de demostración académica.

2.2.2 Propuesta de valor

La propuesta central de Mortuorium es ofrecer una experiencia VR de terror accesible, eliminando la barrera económica del hardware especializado al funcionar sobre un dispositivo móvil de amplia gama dentro de un visor cardboard de bajo costo (menos de $30.000 pesos argentinos).

El diferencial competitivo clave es el uso de ARCore (para android, Arkit para Ios) para convertir el entorno físico real del jugador en el escenario del juego: cada partida es única porque los puntos de aparición de entidades y baterías se determinan dinámicamente según el espacio escaneado. Esto genera una sensación de vulnerabilidad que ningún escenario fijo puede replicar — el terror ocurre literalmente en la zona de confort que el jugador creía segura.

A esto se suman la dimensión cooperativa (hasta 4 jugadores en sesión compartida), la dificultad progresiva en 6 noches, y una mecánica de gestión de recursos (linterna, cordura) que mantiene la tensión durante toda la sesión.

2.2.3 Canales

Google Play Store es el canal principal de distribución: permite distribución masiva, descubrimiento orgánico y gestión de pagos. La descarga gratuita elimina la fricción de adquisición y maximiza el alcance inicial.

Las redes sociales (Instagram, TikTok, YouTube) funcionan como canal de marketing. Las reacciones grupales al jugar en un entorno real son intrínsecamente compartibles; el equipo publicará contenido propio y aprovechará el UGC (user-generated content) de los early adopters.

La Expo universitaria UNLAM actúa como canal de validación y demostración presencial, permitiendo capturar feedback directo de usuarios reales y generar visibilidad dentro del ecosistema académico.

2.2.4 Relaciones con los clientes

El modelo de relación es principalmente: el jugador descarga, instala y comienza a jugar sin intervención del equipo. La experiencia está diseñada para ser autoexplicativa mediante tutoriales y el briefing de noche previo a cada sesión.

La comunidad en redes sociales mantiene el vínculo, comunica actualizaciones y amplifica el contenido generado por usuarios. El canal de YouTube sirve para presentaciones más largas del producto, gameplays y tutoriales. Las demos presenciales en la expo universitaria crean un canal directo de feedback y generan un primer grupo de usuarios interesados.

Los canales de comentarios de Google Play y App store sirven como fuente para recibir feedback de los usuarios, además de nuestras redes sociales. El soporte y las actualizaciones las damos a través del feedback recibido.

2.2.5 Fuentes de ingresos

En la versión académica, el juego se distribuye gratuitamente como producto de demostración universitaria. No existe monetización en esta etapa.

De cara a una eventual versión comercial post-expo, se contemplan dos mecanismos. El primero es una compra única en Play Store para desbloquear el modo multijugador y las noches 4 a 6, manteniendo las primeras tres noches como experiencia gratuita que funciona como demo jugable. El segundo, de mayor potencial a largo plazo, es el licenciamiento del motor de escaneo de entorno y del sistema de entidades a estudios o proyectos terceros que quieran incorporar mecánicas de AR horror sin desarrollarlas desde cero.

2.2.6 Recursos clave

El recurso más importante es el equipo de cuatro desarrolladores con habilidades complementarias en desarrollo, diseño, arte, QA y documentación.

A nivel tecnológico, el stack (Unity, ARCore SDK) es completamente gratuito en su versión académica/personal. Los dispositivos de testing son necesarios para validar el rendimiento en distintas gamas de hardware. Los assets de audio y 3D se obtienen de repositorios libres (Freesound, Sketchfab, Unity Asset Store free tier) o se crean por el equipo.

Durante las pruebas de concepto, se observó que la estabilización de un punto en el espacio funcionaba mucho mejor en dispositivos con iOS, que, en Android, siendo un punto recomendable para avisar al jugador para tener una experiencia en óptimas condiciones, sin omitir sin embargo la jugabilidad para ninguno de los dos sistemas operativos.

2.2.7 Actividades clave

Las actividades centrales son el desarrollo del juego en Unity con soporte VR cardboard, la integración de ARCore para el escaneo espacial y la determinación dinámica de spawns, y la integración del joystick Bluetooth como periférico de control de la linterna.

El diseño y desarrollo de las tres entidades (Sorken, Arbmos y Veleth) con sus comportamientos diferenciados es la actividad de mayor complejidad técnica y creativa. A esto se suma el QA continuo en dispositivos reales (baja, media y alta gama) para reducir el motion sickness, y la optimización del renderizado estereoscópico para mantener la experiencia jugable en la mayoría de los dispositivos del mercado.

2.2.8 Socios clave - Proveedores

Google / ARCore es un socio tecnológico clave: el SDK de mapeo espacial es la base de la mecánica diferencial del juego (escaneo del entorno real). Su disponibilidad gratuita y su mantenimiento activo son condiciones necesarias para el proyecto. Unity Software Inc. provee el motor de desarrollo bajo licencia personal gratuita (mientras el retorno no supere los USD 100000), eliminando uno de los principales costos de un proyecto de videojuego. Si el mismo logra superar este monto, se puede abonar la licencia con lo ganado.

Google Play y App Store de Apple también se consideran socios clave: Son los distribuidores que expondrán el juego en sus servicios para ser descargado. Es fundamental para tener alcance a los dispositivos móviles, ya que los usuarios solo confían en la descarga de aplicaciones a través de estos servicios.

2.2.9 Estructura de costos

El costo dominante es el del equipo: las horas de desarrollo, diseño, testing y documentación que los cuatro integrantes dedican al proyecto a lo largo del cuatrimestre. Aunque no representa un egreso monetario directo, es el insumo más valioso y define el valor del producto.

Los costos monetarios reales son bajos: las licencias de software son gratuitas (Unity Personal, ARCore). Los costos concretos incluyen los dispositivos de prueba adicionales si algún integrante no dispone de un teléfono compatible, el hosting del servidor multijugador (free tier en fase académica), y los joysticks Bluetooth y visores cardboard necesarios para las demos presenciales en la expo universitaria.

Además, podemos agregar los costos de publicar la aplicación por las plataformas de Google Play (pago único) y App Store (pago anual). Además de la comisión que tiene por las ventas, que rondan entre el 15% y 30% (en el Excel del análisis económico financiero, ya está contemplado este descuento en el apartado donde se exponen los ingresos por ventas).

También pretendemos tener una etapa de marketing donde los costos están desarrollados en el Excel de análisis económico financiero.

## 2.3 Oferta - Cuadro de competidores

|     |     |     |     |
| --- | --- | --- | --- |
| Nombre del producto |     |     | Sitio Web | Fortalezas | Debilidades |
| Phasmophobia | [kinetic-games.co.uk](https://kinetic-games.co.uk) | \- Co-op terror hasta 4 jugadores<br><br>\- Gran comunidad activa<br><br>\- Mecánica de investigación innovadora | \- Requiere PC + VR (alto costo).<br><br>\- No es mobile y no usa entorno real del jugador |     |     |
| Five Nights at Freddy's (FNAF) | [steelwoolstudios.com](https://steelwoolstudios.com/) | \- IP icónica con gran reconocimiento Mecánica de "sobrevivir noches" referente del género.<br><br>\- Versión mobile disponible | \- Sin modo co-op<br><br>\- Sin VR mobile ni ARCore<br><br>\- Escenario estático, sin personalización |     |     |
| Dreadhalls | [dreadhallsgame.com](https://www.dreadhalls.com/) | \- VR nativo con atmósfera sólida.<br><br>\- Compatible con visores cardboard.<br><br>\- Buena optimización mobile. | \- Solo jugador, sin co-op<br><br>\- Sin mapeo del entorno real<br><br>\- Experiencia estática, sin mecánicas de gestión |     |     |

## 2.4 Oferta - Productos complementarios

|     |     |     |     |
| --- | --- | --- | --- |
| Nombre del producto |     |     | Sitio Web | Fortalezas | Debilidades |
| Auriculares JBL Tune 770NC | [Jbl.com.ar](https://www.jbl.com.ar/JBLT770NCPURAM.html?gad_source=1&gad_campaignid=18088891609&gbraid=0AAAAAofePV1cy7jjhQvql56h3kkkdvE_w&gclid=Cj0KCQjww8rQBhDjARIsAE43KPO2o8QLEAjE6cm-cIbLrCic4AQ-wi_9L43ZMwSuG68WqGUV18wc6bAaAnc5EALw_wcB) | \- Potencia el audio espacial del juego<br><br>\- Aísla al jugador del entorno exterior con cancelación de ruido<br><br>\- Amplifica la inmersión y el susto | \- Pueden agregar latencia de audio por Bluetooth |     |     |
| Anteojos Vr Box LH3000 | [M](https://www.mercadolibre.com.ar/p/MLA38847724?offer_type=BEST_PRICE&pdp_filters=item_id:MLA1497337447&matt_tool=89488245#origin=share&sid=share&wid=MLA1497337447&action=whatsapp)[ercado Libre](https://www.mercadolibre.com.ar/p/MLA38847724?offer_type=BEST_PRICE&pdp_filters=item_id:MLA1497337447&matt_tool=89488245#origin=share&sid=share&wid=MLA1497337447&action=whatsapp) | \- Comodidad para portar el dispositivo móvil.<br><br>\- Inmersión al tener el dispositivo sobre tu visión | \- Visibilidad del ambiente reducida |     |     |
| Joystick Bluetooth | [Mercado Libre](https://www.mercadolibre.com.ar/control-joystick-p-lente-vr-android-bluetooth-inalambrico/up/MLAU3842526310?pdp_filters=item_id%3AMLA1697881073&from=gshop&matt_tool=46385749&matt_word=&matt_source=google&matt_campaign_id=23390549165&matt_ad_group_id=189479942103&matt_match_type=&matt_network=g&matt_device=c&matt_creative=790066494431&matt_keyword=&matt_ad_position=&matt_ad_type=pla&matt_merchant_id=5348929475&matt_product_id=MLAU3842526310&matt_product_partition_id=2454011831403&matt_target_id=aud-2418879660225:pla-2454011831403&cq_src=google_ads&cq_cmp=23390549165&cq_net=g&cq_plt=gp&cq_med=pla&gad_source=1&gad_campaignid=23390549165&gbraid=0AAAAAD01zQaNp6KwyefxeOwo7rbwrgDWn&gclid=Cj0KCQjww8rQBhDjARIsAE43KPNSjlKWPg8aILIhmgES3YIaMt49P8fTHmjEe1b9lUv7R3-q5pmFfP8aAvdXEALw_wcB) | \- Agregar funcionalidades<br><br>\- Comodidad para interactuar | \- Usa pilas |     |     |

## 2.5 Análisis económico-financiero

Indicar TIR , VAN del proyecto; que inversión se necesita para desarrollar el proyecto y en cuanto tiempo se recupera la inversión

[Análisis Económico Financiero.xlsx](https://ingunlamedu.sharepoint.com/:x:/r/sites/112-Proyecto2026/Documentos%20compartidos/General/Entregas/2026-05-30/An%C3%A1lisis%20Econ%C3%B3mico%20Financiero.xlsx?d=wcdd9028819e4409eaebcef04cc0bd26e&csf=1&web=1&e=uz8jAW)|

_&lt;Ver planilla de cálculo Análisis económico financiero&gt;_

# 3\. Definición de alcance

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 3.1 Visual story Mapping3.2 Product Backlog_Plataforma y hardware_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>usar el juego con mi smartphone Android o iOS dentro de un visor cardboard</p></td><td><p>tener una experiencia VR inmersiva sin hardware costoso</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>conectar un joystick vía Bluetooth al dispositivo</p></td><td><p>controlar la linterna sin tocar la pantalla durante la sesión</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la dirección de la linterna siga la orientación de mi cabeza</p><p></p></td><td><p>explorar el entorno de forma natural mirando hacia donde apunto</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el juego detecte si mi dispositivo es compatible con ARCore</p></td><td><p>saber de antemano si puedo jugar</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>ser informado de la versión óptima del dispositivo para jugar</p></td><td><p>saber de antemano cómo será la jugabilidad según el dispositivo elegido</p></td><td><p>Baja</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Escaneo de espacio físico_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>escanear con la cámara una imagen principal que sirva como punto de referencia</p></td><td><p>Que el juego dimensione el tamaño de la habitación en donde está el jugador y pueda determinar puntos de spawn</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el escaneo de la imagen principal con la cámara sea obligatorio antes de empezar</p></td><td><p>Reducir lo más posible la cantidad de errores de posicionamiento</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el escaneo de la imagen principal, con fines de recalibración, sea opcional durante el juego</p></td><td><p>refrescar y readaptar las dimensiones de la habitación</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el mapa escaneado persista entre noches</p></td><td><p>no tener que repetir el escaneo en cada sesión</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Sistema de linterna y batería_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la linterna consuma batería con el uso continuo</p></td><td><p>tener que gestionar el recurso de forma estratégica</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>Tener una barra en la pantalla, en una posición y diseño que no interfiera con la inmersión, que indique el estado de la batería de la linterna</p></td><td><p>Poder identificar cuanta batería tengo y tomar decisiones en base a ello</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>que la linterna empiece a titilar cuando tenga poca batería y la luz sea más tenue</p></td><td><p>Alarmarme si me estoy quedando sin batería y dando aviso a la amenaza inminente</p></td><td><p></p></td><td><p></p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que se generen baterías de repuesto dispersas en el entorno</p></td><td><p>Poder buscarlas y tener la posibilidad de agarrarlas</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Dada la distancia, la posición de la cámara y a ausencia de obstáculos, poder agarrar las baterías con un botón del joystick.</p></td><td><p>poder recargar la linterna y continuar la noche</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que las baterías no reaparezcan en el mismo punto en menos de 1 minuto</p></td><td><p>que el juego no se vuelva trivial acampando en un spawn</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el temporizador de reaparición se active solo al alejarme del punto</p></td><td><p>que la mecánica de respawn tenga coherencia espacial</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Entidad Sorken_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol><p></p></td><td><p>jugador</p></td><td><p>escuchar ruidos distintos por cada punto de entrada, y que estos tengan un ruido único y reconocible</p></td><td><p>identificar desde dónde intenta ingresar Sorken sin verlo directamente</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>poder ahuyentar a Sorken iluminándolo más de 3 segundos con la linterna</p></td><td><p>tener una mecánica activa de defensa contra la entidad principal</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que si tardo más de 5 segundos en iluminar a Sorken, a una distancia menor a 3 metros cuando este esté intentando ingresar al entorno, éste ingrese</p></td><td><p>Generar el desafío y la urgencia de estar atento a los intentos de ingreso del Sorken y tener una penalización si este ingresa sin que lo detenga satisfactoriamente</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>Que al acercarme o apuntar al punto donde está intentando ingresar el Sorken, la linterna titile y se escuche ruidos</p></td><td><p>Tener feedback de que identificamos correctamente el punto donde se encuentra y agregar tensión a la atmósfera.</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p><p></p></td><td><p>Que cuando el sorken ingrese, me persiga por todo el entorno y para ahuyentarlo, tenga que apuntar con la linterna por 5 segundas</p></td><td><p>Dar una segunda oportunidad de escapar del sorken</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p><p></p></td><td><p>Que cuando el sorken ingrese al entorno, si este me atrapa, me salte una pantalla de muerte</p></td><td><p>Para tener una penalización si esta entidad entra en el entorno y me atrapa</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Sistema de cordura_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>Tener una barra en la pantalla, en una posición y diseño que no interfiera con la inmersión, que indique el estado de mi cordura</p></td><td><p>Poder identificar cuanta cordura tengo y tomar decisiones en base a ello</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que mi cordura disminuya si la linterna está apagada más de 5 segundos</p></td><td><p>sentir presión constante en la gestión de la batería</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que al llegar a cordura cero la interfaz se distorsione visualmente</p></td><td><p>tener retroalimentación clara del estado crítico antes de las consecuencias de la misma.</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la cordura no se pueda recuperar</p></td><td><p>que cada mala decisión sea permanente y aumente la tensión</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Entidad Arbmos_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que quedarme estático, más de 5 segundos, invoque a Arbmos</p></td><td><p>que la inacción tenga consecuencias directas y visibles</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que Arbmos sea letal solo si mi cordura ya está en cero</p></td><td><p>que haya una escalada de consecuencias clara y predecible</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la aparición de Arbmos distorsione la lente en su zona de pantalla</p></td><td><p>tener una señal visual diferenciada de otras entidades</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>escuchar efectos de audio escalables (susurros y gritos) al llegar a cordura cero</p></td><td><p>que el jumpscare final esté precedido de tensión creciente y reconocible</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Entidad Veleth y libro del ritual_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que aparezcan un libro de ritual sobre el código QR colocado de forma horizontal</p></td><td><p>Poder identificarlo y saber su posición dentro del juego</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>Que cada cierto tiempo aleatorio entre 30 y 50 segundos, aparezca una oscuridad que intente consumir el libro</p></td><td><p>Poder identificar que el libro está en peligro y debo defenderlo</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p><p></p></td><td><p>Que cuando apunte con la linterna al libro del ritual, mientras este está siendo consumido por la oscuridad, esta se disipe a los 4 segundos de estar en contacto con la luz de la linterna</p></td><td><p>Poder tener una mecánica en la que puedo proteger el libro de la oscuridad</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p><p></p></td><td><p>Que, si no defiendo exitosamente el libro en un intervalo de 6 segundos, este sea consumido y pierda el libro</p></td><td><p>Para identificar que falle defendiendo el libro del ritual y lo perdí</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que, al perder el libro del ritual, se active un sonido que invoca a Veleth</p></td><td><p>que fallar tenga consecuencias inmediatas y narrativamente coherentes</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que Veleth me persiga activamente hasta atraparme una vez convocada</p></td><td><p>que proteger el libro de ritual sea prioritario y no algo ignorable</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Noches, dificultad y progresión_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>jugar 6 noches con dificultad creciente</p></td><td><p>tener una curva de aprendizaje progresiva y rejugabilidad</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Que, al empezar una noche, este me avise que entidades van a estar presente</p></td><td><p>Saber a que riesgos me expongo y como defenderme</p></td><td><p>Baja</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Que haya un reloj en la pantalla, con un diseño que no afecte la inmersión, que indique el avance del tiempo</p></td><td><p>Saber cuánto tiempo llevo y cuanto falta para que termine la noche</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que el progreso se guarde de forma local (si es multijugador, el host guarda el progreso) entre noches</p></td><td><p>poder retomar la partida sin perder el avance</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la frecuencia del Sorken, la aparición de la oscuridad en el libro, el consumo de batería y la cordura escalen cada noche</p></td><td><p>que cada sesión sea más difícil que la anterior</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la duración objetivo de cada noche sea de 5 a 10 minutos, indicando el fin de la partida (el objetivo es sobrevivir)</p></td><td><p>poder jugar en sesiones cortas sin comprometer la experiencia</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Multijugador y escalado_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol><p></p></td><td><p>jugador</p></td><td><p>crear o unirme a una sesión de 1 a 4 jugadores</p></td><td><p>vivir la experiencia de forma cooperativa con amigos</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Poder crear una sala de espera que se exponga en la LAN</p></td><td><p>Que otros jugadores puedan ingresar a la sesión</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Poder ver que salas de espera están abiertas en la LAN</p></td><td><p>Poder ingresar a la sesión de otro jugador</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Que, si un jugador abandona la sesión, la misma persista en el mismo estado</p></td><td><p>Poder continuar jugando sin necesidad de reiniciar la noche si un jugador se va</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la dificultad escale automáticamente según la cantidad de jugadores (frecuencia del Sorken y el tiempo de alumbrado para que se retire, el consumo de batería y la cordura escalen)</p></td><td><p>que la experiencia no se vuelva trivial al jugar en grupo</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Tener un chat de voz en la sesión</p></td><td><p>Poder comunicarme con mis compañeros, debido al uso de auriculares y a los sonidos, pueden no escucharse claro sin el mismo</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>hacer ping a un punto usando la linterna y el botón secundario</p></td><td><p>coordinarme con el equipo incluso si el audio de voz falla</p></td><td><p>Baja</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Infraestructura técnica_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>desarrollador</p></td><td><p>implementar la lógica del juego con arquitectura cliente-servidor</p></td><td><p>poder agregar el modo multijugador en el Hito 3 sin refactoring mayor</p></td><td><p>Alta</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>Jugador</p></td><td><p>Poder configurar distintas opciones como: brillo, aberración, sonido, reducir efectos, motion sickness</p></td><td><p>Poder adaptar el juego a mi confort</p></td><td><p>Baja</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

1.  _Efectos visuales y sonoros_

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>ID Historia de usuario</p></td><td><p>Como</p></td><td><p>Quiero</p></td><td><p>Para</p></td><td><p>Prioridad</p></td><td><p>Status</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>ver un filtro de VHS o cámara antigua sobre toda la imagen</p></td><td><p>que la atmósfera visual refuerce la sensación de tensión constante</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>ver aberración cromática y distorsión de lente en momentos de tensión</p></td><td><p>Para dificultar mi visión y generarme tensión al no tener la pantalla clara</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr><tr><td><ol><li></li></ol></td><td><p>jugador</p></td><td><p>que la banda sonora ambiental este acorde a cada evento o nivel de peligro</p></td><td><p>no depender solo de los efectos puntuales y generar una mejor inmersión y tensión en cada evento</p></td><td><p>Media</p></td><td><p>Por hacer</p></td></tr></tbody></table></div>

## 3.3 Criterios de aceptación Historia de Usuario: US-1.1

<div class="joplin-table-wrapper"><table><tbody><tr><td><p><strong>N° Criterio de aceptación</strong></p></td><td><p><strong>Dado...</strong></p></td><td><p><strong>Cuando...</strong></p></td><td><p><strong>Entonces...</strong></p></td></tr><tr><td><p>1</p></td><td><p>El usuario acaba de descargar e instalar el juego</p></td><td><p>Inicie el juego por primera vez</p></td><td><p>El juego debe verificar que la versión del dispositivo NO sea inferior a las siguientes:</p><ul><li>Android: 14</li><li>iOS: 17</li></ul><p>Esto para garantizar una jugabilidad óptima. Cualquier otro sistema operativo o versión inferior no es aceptado y debe mostrarse un popup con el mensaje “Lo sentimos, la versión de su dispositivo no es compatible con el juego.”</p></td></tr><tr><td><p>2</p></td><td><p>El chequeo de la versión resultó exitoso y posee</p></td><td><p></p></td><td><p>El juego debe sincronizar y refrescar su mapa virtual, readaptando las paredes a su nueva ubicación y corrigiendo desvíos de más de 50cm.</p><p>La tasa de refresco no puede ser mayor a 30 segundos, es decir, si se escaneó el código QR una vez, no se volverá a habilitar la sincronización espacial hasta por lo menos 30 segundos para evitar procesamiento innecesario.</p></td></tr></tbody></table></div>

## Historia de Usuario: US-1.2

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El joystick Bluetooth está vinculado al dispositivo | Inicie el juego | debe detectar automáticamente el joystick y habilitar sus controles. |
| 2   | El joystick se desconecta durante la partida | El jugador intenta utilizar sus controles | El juego debe pausar la partida e informar la desconexión, y permitir reconectarlo sin cerrar la aplicación, reanudando la partida. |

## Historia de Usuario: US-1.3

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador se encuentra dentro de una partida | Gira la cabeza en cualquier dirección | La linterna debe orientar su haz de luz siguiendo la rotación de la cámara con una latencia imperceptible para el jugador. |

## Historia de Usuario: US-1.4

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador se encuentra dentro de una partida | Gira la cabeza en cualquier dirección | La linterna debe orientar su haz de luz siguiendo la rotación de la cámara con una latencia menor a 100 ms. |

Historia de Usuario: US-2.1

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El inicio del juego, sin haber escaneado un código QR | Escanee un código QR con la cámara del dispositivo, al inicio de la noche | Quiero que el juego reconozca el ambiente y las paredes, pudiendo formar un mapa virtual del tamaño en donde se encuentra el jugador, para poder ubicar correctamente los puntos de spawn, ubicando cada pared con una desviación no mayor a 50cm de las paredes reales. |
| 2   | Un juego iniciado, en el medio de cualquier noche | La cámara del jugador vuelva a reconocer el código QR de punto de referencia | El juego debe sincronizar y refrescar su mapa virtual, readaptando las paredes a su nueva ubicación y corrigiendo desvíos de más de 50cm.<br><br>La tasa de refresco no puede ser mayor a 30 segundos, es decir, si se escaneó el código QR una vez, no se volverá a habilitar la sincronización espacial hasta por lo menos 30 segundos para evitar procesamiento innecesario. |

## Historia de Usuario: US-2.2

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Que el jugador aún no escaneó la imagen principal de referencia | Intenta comenzar una partida | El juego no debe permitir iniciar la noche y debe solicitar el escaneo del código QR o una imagen principal de referencia. |
| 2   | El escaneo finaliza correctamente | El jugador presiona "Comenzar" | El juego debe habilitar inmediatamente el inicio de la partida y guardar el modelo del entorno de forma local. |

## Historia de Usuario: US-2.3

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador se encuentra en una partida | Vuelve a escanear la imagen principal de referencia | El juego debe recalibrar el mapa virtual sin reiniciar la partida. |

## Historia de Usuario: US-2.4

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador finaliza una noche | Quiera jugar la siguiente noche | El juego debe mostrar los guardados locales de modelos de entorno y permitir reutilizarlos. |

## Historia de Usuario: US-3.1

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador tiene la linterna encendida | Transcurre el tiempo | La batería debe disminuir de forma continua hasta agotarse. |
| 2   | La batería llega al 0% | El jugador intenta utilizar la linterna | La linterna debe apagarse automáticamente y no podrá volver a encenderse hasta recargar una batería. |

## Historia de Usuario: US-3.3

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | La batería desciende por debajo del 20% | El jugador continúa utilizando la linterna | La luz debe comenzar a titilar de forma intermitente. Además, la intensidad de la luz debe reducirse progresivamente hasta agotarse completamente. |

## Historia de Usuario: US-3.4

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Existe al menos un punto de aparición disponible | Pasan 30 segundos entre generación de baterías | Debe aparecer una batería en un punto aleatorio válido del entorno escaneado. |
| 2   | Una batería ya se encuentra disponible en un punto | El sistema intenta generar otra batería | No deberá generarse una segunda batería sobre el mismo punto mientras la anterior permanezca disponible. |

## Historia de Usuario: US-3.5

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador apunta a una batería sin obstáculos entre ambos a una distancia menor a medio metro. | Presiona el botón de interacción del joystick | La batería debe ser recogida y utilizada para recargar la linterna. |

## Historia de Usuario: US-3.6

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Que existen 3 baterías generadas en el entorno que aún no fueron recogidas | Se intenta generar otro batería pasado 30 segundas desde la última vez que se intentó generar | No se generará la batería |

## Historia de Usuario: US-3.7

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Que el jugador se encuentra en un radio de 1 metro del punto de aparición de una batería | Se intenta generar una batería en ese mismo punto de aparición | No se generará la batería |

## Historia de Usuario: US-4.2

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Sorken está intentando ingresar al entorno | El jugador ilumina a Sorken durante al menos 3 segundos continuos | Sorken debe retirarse y finalizar el intento de ingreso |
| 2   | El jugador deja de iluminar a Sorken antes de completar los 3 segundos | El haz de luz deja de apuntarlo | El contador de tiempo se mantiene y el jugador deberá volver a iluminarlo durante el tiempo restante. |

## Historia de Usuario: US-4.3

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Sorken está intentando ingresar a una distancia menor o igual a 3 metros | Transcurren más de 5 segundos sin que el jugador logre ahuyentarlo | Sorken debe ingresar al entorno. |

## Historia de Usuario: US-4.5

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | Que el usuario no pudo evitar que el sorken ingrese al entorno | Este ya está en el entorno | El Sorken perseguirá al usuario por el entorno, rodeando obstáculos, a una velocidad de 0.5m / s |
| 2   | El sorken este dentro del entorno | El usuario apunte con la linterna en dirección al sorken a una distancia menor a 3 metros por más de 5 segundos | El Sorken debe retirarse del entorno y vuelve a permitir que intente ingresar |
| 3   | El jugador deja de iluminar a Sorken, una vez dentro del entorno, antes de completar los 5 segundos | El haz de luz deja de apuntarlo | El contador de tiempo se mantiene y el jugador deberá volver a iluminarlo durante el tiempo restante. |

## Historia de Usuario: US-4.6

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El Sorken está dentro del entorno persiguiendo al usuario | Este esté a menos de medio metro del usuario | El Sorken habrá alcanzado al usuario, y a este se le generará una ventana indicando que murió |

## Historia de Usuario: US-5.2

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | La linterna permanece apagada durante más de 3 segundos | Continúa transcurriendo la partida | Según la configuración de la partida, se le debe restar cierto porcentaje de cordura por cada 3 segundos más que este con la linterna apagada |

## Historia de Usuario: US-5.4

|     |     |     |     |
| --- | --- | --- | --- |
| **N° Criterio de aceptación** | **Dado...** | **Cuando...** | **Entonces...** |
| 1   | El jugador pierde cordura por cualquier evento del juego que te la quite | Finaliza el evento | El valor de cordura debe disminuir y no podrá aumentar nuevamente durante esa partida. |
| 2   | El usuario avanza de noche | Comienza una nueva noche | El valor de cordura se reinicia a 100% |

## 3.4 Criterios de completado.

<div class="joplin-table-wrapper"><table><tbody><tr><td><p>Ejemplo:</p><p>Consideramos que una historia está DONE cuando se cumplen las siguientes condiciones:</p><p></p><ul><li>La documentación de la historia y el código tuvieron revisiones de pares</li><li>Ver Checklist para documentación de User Stories</li><li>El código de la historia está mergeado en el Branch de QA</li><li>Todos los tipos de tests automatizados que se definieron sobre la historia funcionan correctamente</li><li>Los CA principales están automatizados contra Mocks</li><li>Se realizó el Test Funcional End to End de la historia apuntando a los servicios reales</li><li>La historia no tiene bugs</li><li>La historia fue mostrada y aprobada por el PO</li></ul><p></p></td></tr></tbody></table></div>

# 4\. Equipo e Interesados

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 4.1 Equipo de proyecto

|     |     |
| --- | --- |
| Rol de Scrum | Nombre y apellido |
| Product Owner | Cristian Marcelo Rios |
| Scrum master | Franco Nicolas Vallejos |
| Desarrollador | Máximo Augusto Bosch |
| Desarrollador | Giuliano Di Tommaso |

## 4.2 Matriz de interesados

|     |
| --- |
| Nombre Interesado |
| Empresa Google (ARCore) |
| Contacto [Google for developers](https://developers.googleblog.com/search/?q=arcore&max-results=12) |

|     |
| --- |
| Nombre Interesado |
| Empresa Unity |
| Contacto [Unity Support](https://unity.com/contact-us) |

|     |
| --- |
| Nombre Interesado |
| Empresa Google (Google play) |
| Contacto [Google Play support](https://support.google.com/googleplay/?hl=es&sjid=5969983941537398067-SA#topic=3364260) |

|     |
| --- |
| Nombre Interesado |
| Empresa Apple (App Store) |
| Contacto 1-800-275-2273 (USA) |

# 5\. Release planning - Plan de versiones

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 5.1 Estimación Story Points

Que técnica utilizan. Por ej: planning poker, Fibonacci (1,2,3,5,8,13,21) o talles de camiseta (chico medio grande extra grande)

Para estimar las historias de usuario se utilizará la técnica de Planning Poker, empleando la siguiente escala de Story Points:

1, 2, 3, 5, 8, 13, 20, 40 y 100

|     |     |
| --- | --- |
| Story Points | Interpretación |
| 1   | Muy baja complejidad |
| 2   | Baja complejidad |
| 3   | Complejidad moderada |
| 5   | Complejidad media |
| 8   | Complejidad alta |
| 13  | Muy alta complejidad |
| 20  | Historia grande con incertidumbre |
| 40  | Épica o funcionalidad muy compleja |
| 100 | Funcionalidad extremadamente compleja que requiere dividirse |

Estimación por épica

|     |     |
| --- | --- |
| Épica | Story Points |
| Plataforma y Hardware | 13  |
| Escaneo de espacio físico | 20  |
| Sistema de linterna y batería | 13  |
| Entidad Sorken | 13  |
| Sistema de cordura | 13  |
| Entidad Arbmos | 13  |
| Entidad Veleth | 20  |
| Noches, dificultad y progresión | 8   |
| Multijugador y escalado | 40  |
| Infraestructura técnica | 40  |
| Efectos visuales y sonoros | 20  |
| **Total** | 188 |

## 5.2 Plan de versiones

En el archivo Excel [Gantt.xlsx](https://ingunlamedu.sharepoint.com/:x:/r/sites/Proyecto26/Documentos%20compartidos/General/Gantt.xlsx?d=w9555ea79349945e9bd3f338cd69139cf&csf=1&web=1&e=Pb9ZvG) se encuentra detallado el cronograma de entregables definido para este proyecto.

|     |     |
| --- | --- |
| Release | Épicas |
| Release 1 - Escaneo de entorno | 1\. Plataforma y Hardware<br><br>2\. Escaneo de espacio físico |
| Release 2 - Prototipo sorken | 3\. Sistema de linterna y baterías<br><br>4\. Entidad Sorken |
| Release 3 - Prototipo Arbmos | 5\. Sistema de cordura<br><br>6\. Entidad Arbmos |
| Release 4 - Prototipo Veleth | 7\. Entidad Veleth |
| Release 5 - Beta single player | 8\. Noches, dificultad y progresión |
| Release 6 - Alpha | 9\. Multijugador y escalado<br><br>10\. Infraestructura técnica<br><br>11\. Efectos visuales |

# 6\. Plan de comunicaciones

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 6.1 Comunicaciones del proyecto

|     |     |     |     |     |
| --- | --- | --- | --- | --- |
| Mensaje | Audiencia / Destinatario | Método / Medio | Frecuencia | Remitente del Mensaje |
| Describe la información a ser comunicada | Detalla el o los destinatarios del mensaje (Ej: Equipo del proyecto) | Describe cómo será entregada dicha información (Ej: Escrito / vía e-mail) | Indica con qué frecuencia se envía dicha información (Ej: Quincenal) | Detalla el o los responsables de elaborar el mensaje |
| Daily meeting | Equipo de proyecto | WhatsApp (Asincrónico) | Diaria | Scrum master |
| Sprint Planning | Equipo de proyecto | WhatsApp / Microsoft Teams | Cada 2 semanas | Scrum master |
| Comunicaciones formales de la cátedra | Alumnos | Microsoft Teams / Presencial | A demanda | Equipo docente |
| Sprint Review / Demo | Equipo de proyecto y tutores | Microsoft Teams / Presencial | A demanda | Tutores |
| Reporte de avance a tutores | Equipo de proyecto y tutores | Microsoft Teams | Cada semana | Tutores |
| Retrospectiva de hito (cierre de cada Hito del plan a alto nivel) | Equipo de proyecto | WhatsApp / Microsoft Teams | Por hito (3-4 semanas) | Product Owner |

# 7\. Experiencia de Usuario

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 7.1 Prototipo de navegación

También se encuentra en: Entregables > {fecha} > Componentes & Navegación > Navegacion.pdf

## 7.2 Wireframes Mockups

También se encuentra en: Entregables > {fecha} > Componentes & Navegación > Menus & Onboarding.pdf

También se encuentra en: Entregables > {fecha} > Componentes & Navegación > Single Player.pdf

También se encuentra en: Entregables > {fecha} > Componentes & Navegación > Multiplayer & HUD.pdf

## 

# 8\. Arquitectura de Software

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 8.1 Diagrama de Arquitectura

_Es un diagrama que representa las ideas preestablecidas y los módulos/componentes candidatos de un sistema o arquitectura. P_

_Es importante mostrar cómo los componentes se distribuyen entre las capas y cómo estas se comunican entre sí._

## 8.2 Frameworks / Componentes utilizados

_&lt;Mencionar / detallar componentes, bibliotecas javascripts, frameworks java, php, etc que utilice el sistema. Sólo se debe nombrar los Frameworks que la aplicación que está describiendo utilice directamente. El objetivo de esta sección es dejar marcado de forma clara cuáles son las dependencias externas del sistema&gt;._

## 8.3 Infraestructura tecnológica

_&lt;Describir sistemas operativos, bases de datos utilizadas, productos de middleware implicados, entre otros…&gt;._

|     |     |     |     |
| --- | --- | --- | --- |
| **Descripción** |     |     |     |
| **Sistema Operativo** |     |     |     |
| **Bases de Datos** |     |     |     |
| **AppServer / WebServer** |     |     |     |
| **Lenguajes utilizados** |     |     |     |

_En caso de utilizar AWS, Azure o cualquier otro tipo de nube indicar nomenclatura de servicios utilizados_

## 8.4 Otros diagramas según corresponda

_&lt;Pueden poner DER , Diagrama de clases u otro diagrama que quieran incluir&gt;_

## 8.5 Estándar de codificación

&lt;Indicar estándar de codificación&gt;

&lt;Indicar Semantic Versioning&gt; ej: https://semver.org/

# 9\. Plan de pruebas

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

## 9.1 Diseño y ejecución de escenario de prueba

|     |     |     |     |
| --- | --- | --- | --- |
| **Historia de usuario** | **Escenario de prueba** | **Resultado** | **Observaciones** |
| Historia #1 - Criterio de aceptación x | Verificar que ante x situación, suceda | Aprobado/Fallo | En caso de falla, indicar descripción a través de Issue tracker. |
| ... | ... | ... | ... |

## 9.2 Seguimiento de fallas

Para seguimiento issues, utilizar la herramienta que deseen y volcar la información aquí.

# 10\. Manual Instalación

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

Describir el proceso de despliegue de la solución.

# 11\. Hoja de Ruta

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

_Incluir en una imágen la hoja de ruta a presentar_

# Anexos I. Retrospectiva

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

<div class="joplin-table-wrapper"><table><tbody><tr><td colspan="2"><p>DD-MM-AAAA - <strong>Retrospectiva #1</strong></p></td></tr><tr><td><p><strong>¿Qué hicimos bien?</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>¿Qué podemos mejorar?</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>Ideas y propuestas</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>ROI </strong>(retorno de la inversión) <em>Indicar número del 2 al -2 de como fué la retrospectiva respecto al tiempo invertido</em></p></td><td><p></p><p></p><p></p><p></p></td></tr></tbody></table></div>

# 

<div class="joplin-table-wrapper"><table><tbody><tr><td colspan="2"><p>DD-MM-AAAA - <strong>Retrospectiva #1</strong></p></td></tr><tr><td><p><strong>¿Qué hicimos bien?</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>¿Qué podemos mejorar?</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>Ideas y propuestas</strong></p></td><td><ul><li></li><li></li><li></li><li></li></ul></td></tr><tr><td><p><strong>ROI </strong>(retorno de la inversión) <em>Indicar número del 2 al -2 de como fué la retrospectiva respecto al tiempo invertido</em></p></td><td><p></p><p></p><p></p><p></p></td></tr></tbody></table></div>

# Anexos II. Riesgos

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

|     |     |     |
| --- | --- | --- |
| Descripción del riesgo | Criticidad | Responsable |
|     |     |     |
|     |     |     |
|     |     |     |

# Anexos III. Minuta de reunión

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

**_Minutas de reunión #1_**

**_ASISTENTES_**

|     |     |     |
| --- | --- | --- |
| _NOMBRE Y APELLIDO_ | _ÁREA/SECTOR_ | _PRESENTE:_ **_SI/NO_** |
|     |     |     |
|     |     |     |

**_OBJETIVO DE LA REUNIÓN:_**

**_DATOS DE LA REUNIÓN_**

_FECHA:_

_MINUTA ELABORADA POR:_

**_TEMAS TRATADOS Y PRINCIPALES ACUERDOS:_**

**_SEGUIMIENTO Y TEMAS PENDIENTES:_**

|     |     |     |
| --- | --- | --- |
| _RESPONSABLE_ | _COMPROMISO_ | _FECHA DE VENCIMIENTO_ |
| _..._ | _..._ | _..._ |

**_PRÓXIMA REUNIÓN:_** _A definir._

# Anexos IV. Paper investigación

**Historia de Revisión**

|     |     |     |     |
| --- | --- | --- | --- |
| **Fecha** | **Versión** | **Descripción** | **Autor** |
|     |     |     |     |
|     |     |     |     |

_Incluir en una imagen el póster técnico e incluir el paper de investigación si aplica._