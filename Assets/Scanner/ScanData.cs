using System;
using System.Collections.Generic;
using UnityEngine;

// DTOs serializables (via JsonUtility). Todas las posiciones/rotaciones son
// anchor-relativas — al cargar un scan, se reconstruyen los GameObjects parentados
// a WorldOrigin con esos local-transforms.
namespace Scanner
{
    [Serializable]
    public class Vec3
    {
        public float x, y, z;
        public Vec3() { }
        public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public class Quat
    {
        public float x, y, z, w;
        public Quat() { }
        public Quat(Quaternion q) { x = q.x; y = q.y; z = q.z; w = q.w; }
        public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
    }

    [Serializable]
    public class DoorData
    {
        public string id;
        public float uMin, uMax;  // a lo largo del eje base de la pared (0..|b-a|)
        public float vMin, vMax;  // vertical desde la base (0..height)
    }

    [Serializable]
    public class WallData
    {
        public string id;
        public string polylineId;
        public Vec3 aLocal;
        public Vec3 bLocal;
        public float height;
        // Grosor de la caja 3D (m). La pared se extruye desde la cara cercana
        // (la linea aLocal->bLocal) a lo largo de su normal horizontal.
        public float width;
        // Lado de extrusion: +1/-1 sobre cross(up, baseHat). Se decide al crear
        // el primer segmento de la polilinea (apuntando lejos de la camara) y lo
        // comparten todos los segmentos de esa polilinea.
        public int side = 1;
        public List<DoorData> doors = new List<DoorData>();
    }

    [Serializable]
    public class CubeData
    {
        public string id;
        public Vec3 posLocal;
        public Quat rotLocal;
        public Vec3 scaleLocal;
        // Signo (±1 por eje) de la esquina marcada como "A". Define donde quedan las
        // esferas de edicion (la diagonal real que marco el usuario). Si falta o es
        // (0,0,0) — escaneos viejos — se usa la diagonal por defecto (1,1,1).
        public Vec3 cornerSignA;
    }

    [Serializable]
    public class ScanData
    {
        public const string CurrentVersion = "1";
        public string version = CurrentVersion;
        public string name;
        public List<WallData> walls = new List<WallData>();
        public List<CubeData> cubes = new List<CubeData>();

        // Ancho físico real (en metros) de la imagen de referencia capturada con
        // la cámara. La imagen en sí se guarda como PNG hermano (<name>.png);
        // refImageWidthMeters > 0 indica que hay imagen asociada al escaneo.
        public float refImageWidthMeters;
    }
}
