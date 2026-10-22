# Mortuorium — Mejoras potenciales

Ideas analizadas que todavía **no** se implementaron. Cada una explica el problema, la
causa en el código y la propuesta, para que cualquiera la pueda retomar. No son tareas
comprometidas del sprint (esas están en `TAREAS-PENDIENTES.md`).

---

## Ubicar la imagen de referencia midiendo la distancia con un rayo

*Agregada el 2026-10-21. Área: calibración (`Assets/AR/ARImageAnchor.cs`,
`Assets/Scanner/ReferenceCaptureUI.cs`).*

### Síntoma
Al encontrar la imagen de referencia, a veces la esfera del origen aparece **en la
dirección correcta pero mucho más lejos** de donde está la imagen real. Si después se usa
AJUSTAR ORIGEN → RECENTRAR ACÁ apuntando al mismo lugar, el origen queda a la distancia y
altura correctas.

### Causa
Los dos caminos calculan la distancia de forma distinta:

- **RECENTRAR ACÁ** (`ManualCalibration.CentrarBajoLaMira`) tira un rayo desde el centro de
  la pantalla contra las superficies reales que conoce el AR (`RaycastResolver`): mide la
  distancia.
- **Detección de la imagen**: ARKit/ARCore no miden la distancia, la **deducen del tamaño
  físico** guardado (`refImageWidthMeters`) comparado con lo que se ve en cámara. Si ese
  ancho es mayor que el real, la imagen se ubica proporcionalmente más lejos, sobre la
  misma línea de visión.

El ancho queda mal porque, al capturar, `ReferenceCaptureUI.EstimateWidthMeters` estima la
distancia con `RaycastResolver.ResolveFromScreenPoint`. Si el AR todavía no reconoció
ninguna superficie (común al empezar un escaneo o con la imagen sobre una superficie lisa),
el resolver devuelve su punto de **fallback** a una distancia fija de 2 m
(`RaycastResolver._fallbackDistance`), y el ancho se calcula como si la imagen estuviera
ahí. Con la imagen real a 50 cm, el ancho sale unas 4 veces más grande y la detección la
pone unas 4 veces más lejos.

Mientras tanto, el jugador puede corregirlo ajustando "ANCHO REAL DE LA IMAGEN" en el paso
de confirmar.

### Propuesta
1. **Al capturar:** no usar el fallback para estimar el ancho. Si no hay superficie real,
   avisar en el paso de confirmar ("No pude medir la distancia: ajustá el ancho real") o
   esperar a que el AR reconozca la superficie antes de permitir capturar.
2. **Al detectar la imagen (siempre):** cuando la ventana de convergencia ubica el ancla,
   tirar un rayo desde la cámara hacia la posición detectada. Si choca con una superficie
   real, poner el ancla en ese punto, sobre la misma línea. El rumbo sigue saliendo de la
   imagen; sólo se corrige la distancia.
   - Aceptar la corrección sólo si la superficie es coherente con la orientación guardada
     de la imagen (`refImageOrientation`: horizontal → superficie con normal hacia arriba;
     vertical → normal horizontal). Así se descartan objetos o personas que se crucen.
   - Aceptar sólo si la distancia medida está en un rango razonable de la del AR (por
     ejemplo, entre ×0,25 y ×4) y si el impacto no es el fallback.
   - Si no hay superficie válida, dejar la distancia del AR, como hoy.
3. **Opcional:** con la distancia real, recalcular el ancho verdadero de la imagen y
   guardarlo en el escaneo, para que las próximas detecciones salgan bien desde el inicio
   (corrige solos los escaneos viejos con el ancho mal guardado).
4. **Para verificarlo:** mostrar en el HUD de debug "distancia corregida: de X m a Y m".

### Riesgos
Toca el corazón de la calibración (`ARImageAnchor`), que ubica todo el mapa. Con las
validaciones, el peor caso queda igual que hoy, pero conviene probarlo con varios
escaneos (imagen en piso, mesa y pared; cuarto iluminado y oscuro) antes de mergearlo.
Respetar los invariantes de `ARImageAnchor` descritos en `CLAUDE.md` (sección
"AR anchoring"). Estimación: ~1,5 h.
