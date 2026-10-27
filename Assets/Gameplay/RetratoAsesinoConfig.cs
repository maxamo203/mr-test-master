using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Valores del retrato del asesino al morir (LocalDeath): encuadre, distancias, luces y tiempos.
    // Vive en Resources/RetratoAsesinoConfig.asset para tunearlo sin recompilar. El encuadre y las
    // luces van POR ENTIDAD (porEntidad: Sorken, Arbmos, Veleth tienen modelos de alto, fondo y
    // material muy distintos); una entidad sin entrada propia usa 'general'. Los tiempos son
    // comunes. Se edita y se previsualiza cada entidad por separado con la ventana
    // Mortuorium > Retrato del asesino > Previsualizador (RetratoAsesinoWindow); con el Play
    // corriendo los cambios se ven al instante. Sin el asset rigen los valores por defecto de acá.
    [CreateAssetMenu(fileName = "RetratoAsesinoConfig", menuName = "Mortuorium/Retrato del asesino")]
    public class RetratoAsesinoConfig : ScriptableObject
    {
        public const string RutaResources = "RetratoAsesinoConfig";

        // Entidades que pueden matar. Mismos valores que EntityTypeIds.
        public enum Entidad : byte
        {
            Sorken = EntityTypeIds.Sorken,
            Arbmos = EntityTypeIds.Arbmos,
            Veleth = EntityTypeIds.Veleth,
        }

        [Serializable]
        public class Ajuste
        {
            [Header("Encuadre")]
            [Tooltip("Parte del alto del asesino que se encuadra, desde arriba. 0.2 ≈ la cabeza; 1 = el cuerpo entero.")]
            [Range(0.05f, 1f)] public float encuadreAlto = 0.2f;
            [Tooltip("Aire alrededor de lo encuadrado. 1 = justo; más = más lejos; menos = más cerca (se sale del cuadro).")]
            [Range(0.2f, 3f)] public float margen = 1.3f;
            [Tooltip("Corrimiento vertical del encuadre, en fracción de lo encuadrado (+ sube la mira, el asesino baja en pantalla).")]
            [Range(-1f, 1f)] public float corrimientoVertical = 0f;
            [Tooltip("Corrimiento lateral, en fracción de lo encuadrado (+ = el asesino se corre a la derecha en pantalla). " +
                     "En coords del asesino, no de la pantalla: así es el mismo en vertical, en Cardboard y en el editor.")]
            [Range(-1f, 1f)] public float corrimientoLateral = 0f;
            [Tooltip("Campo visual vertical (grados) con el que se calcula a qué distancia entra lo encuadrado. " +
                     "Fijo a propósito: con el de la cámara real (celular vertical, Cardboard horizontal, editor) " +
                     "el retrato quedaba a una distancia distinta en cada uno. 60 = el de la cámara del editor.")]
            [Range(20f, 150f)] public float campoVisualReferencia = 60f;

            [Header("Pose")]
            [Tooltip("Clip (Generic) del que se toma la pose del retrato. Vacío = el primer cuadro del estado " +
                     "inicial de su Animator (Veleth: reptando), igual en la muerte que en el previsualizador. " +
                     "Con uno, el retrato sale en ese cuadro del clip.")]
            public AnimationClip poseClip;
            [Tooltip("Momento del clip, 0 = principio, 1 = final.")]
            [Range(0f, 1f)] public float poseMomento = 1f;
            [Tooltip("Giro extra del modelo en el retrato (grados, en sus ejes: X cabeceo, Y giro, Z inclinación). " +
                     "Para entidades que no están paradas cuando matan (Veleth repta): con X se la endereza hacia la cámara.")]
            public Vector3 rotacion = Vector3.zero;

            [Header("Distancia (m)")]
            [Range(0.05f, 2f)] public float distanciaMin = 0.3f;
            [Range(0.5f, 8f)] public float distanciaMax = 4f;
            [Tooltip("Que nada del cuerpo quede más cerca que el plano de recorte cercano (si no, lo que sobresale " +
                     "hacia la cámara — alas, brazos — se corta en polígonos). Es lo que no deja acercar a un modelo " +
                     "con los brazos adelante: apagado, manda sólo el encuadre y la distancia mínima.")]
            public bool evitarCorte = true;
            [Tooltip("Aire entre lo que sobresale hacia la cámara y el plano de recorte cercano.")]
            [Range(0f, 0.5f)] public float margenCorte = 0.1f;

            [Header("Look")]
            [Range(0f, 5f)] public float temblorGrados = 0.7f;
            [Tooltip("Luz blanca puntual pegada a la cámara.")]
            [Range(0f, 8f)] public float luzIntensidad = 2f;

            [Header("Luz roja desde abajo")]
            [Tooltip("Luz puntual debajo de lo encuadrado, que ilumina al asesino desde abajo.")]
            public bool luzRojaActiva = true;
            public Color luzRojaColor = new Color(1f, 0.06f, 0.03f);
            [Range(0f, 10f)] public float luzRojaIntensidad = 3f;
            [Tooltip("Alcance de la luz (m). Lo que quede fuera no se tiñe.")]
            [Range(0.1f, 5f)] public float luzRojaRango = 1.2f;
            [Tooltip("Cuánto más abajo que el centro de lo encuadrado (m). Más = luz más rasante, más sombra en la cara.")]
            [Range(0f, 2f)] public float luzRojaAbajo = 0.4f;
            [Tooltip("Cuánto hacia la cámara desde el centro de lo encuadrado (m). 0 = justo debajo; más = ilumina más de frente.")]
            [Range(-1f, 1.5f)] public float luzRojaAdelante = 0.2f;

            [Header("Parpadeo de la luz roja")]
            [Tooltip("Que la luz roja titile en vez de quedar fija.")]
            public bool parpadeoActivo = true;
            [Tooltip("Velocidad del titileo (veces por segundo, aprox.). Bajo = respira; alto = chisporrotea.")]
            [Range(0.5f, 40f)] public float parpadeoVelocidad = 12f;
            [Tooltip("Cuánto baja la intensidad al titilar. 0 = nada; 1 = llega a apagarse.")]
            [Range(0f, 1f)] public float parpadeoProfundidad = 0.5f;
            [Tooltip("Apagones por segundo, en promedio: cortes bruscos, como una lámpara que falla. 0 = ninguno.")]
            [Range(0f, 10f)] public float parpadeoApagones = 1.5f;
            [Tooltip("Cuánto dura cada apagón (s).")]
            [Range(0.01f, 0.5f)] public float parpadeoApagonDuracion = 0.06f;

            public Ajuste Clonar() => (Ajuste)MemberwiseClone();
        }

        [Serializable]
        public class AjusteEntidad
        {
            public Entidad entidad;
            public Ajuste ajuste = new();
        }

        [Header("Tiempos (s) — comunes a todas las entidades")]
        [Tooltip("Lo que tarda en deslizarse desde donde estaba el asesino hasta su lugar.")]
        [Range(0.1f, 4f)] public float revelar = 1.45f;
        [Tooltip("Cuánto se lo deja ver, ya quieto, antes de que desaparezca y salga la pantalla de muerte.")]
        [Range(0f, 10f)] public float sostener = 1f;

        [Tooltip("Para un asesino sin ajuste propio en 'porEntidad'.")]
        public Ajuste general = new();
        public List<AjusteEntidad> porEntidad = new();

        // Ajuste para el typeId de red del asesino (EntityTypeIds); 'general' si no tiene propio.
        public Ajuste Para(byte typeId)
        {
            foreach (var e in porEntidad)
                if (e != null && e.ajuste != null && (byte)e.entidad == typeId) return e.ajuste;
            return general;
        }

        public AjusteEntidad Entrada(Entidad entidad)
        {
            foreach (var e in porEntidad)
                if (e != null && e.entidad == entidad) return e;
            return null;
        }

        private static RetratoAsesinoConfig _actual, _defaults;

        public static RetratoAsesinoConfig Actual
        {
            get
            {
                // Sin asset se usan los defaults, pero se sigue buscando el asset: así uno
                // creado con el menú en medio del Play se toma sin reiniciar.
                if (_actual == null) _actual = Resources.Load<RetratoAsesinoConfig>(RutaResources);
                if (_actual != null) return _actual;
                if (_defaults == null) _defaults = CreateInstance<RetratoAsesinoConfig>();
                return _defaults;
            }
        }
    }
}
