#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Selector de los fondos alternativos del banner (objeto "Fondos alternativos", lo arma
// Mortuorium > Banner > Agregar fondos alternativos). Prende un solo grupo a la vez para
// compararlos; los ojos se pueden sumar encima de cualquiera. Cada grupo es un objeto
// normal: se mueve, escala o se le apagan partes desde la jerarquía.
//
// TIER: editor-only (archivo entero bajo #if UNITY_EDITOR).
[ExecuteAlways]
public class BannerFondo : MonoBehaviour
{
    public enum Variante { Original, Ventanal, Sigilo, Habitacion, Ojos, Foto, Living }

    public Variante variante = Variante.Ventanal;
    [Tooltip("Suma los ojos en la oscuridad encima del fondo elegido.")]
    public bool ojosEncima;

    [Header("Foto y Living")]
    [Tooltip("Niebla azul oscura en vez de la del panel Iluminación, así el piso y el humo se funden con el cuarto.")]
    public bool nieblaAzul = true;
    public Color colorNieblaAzul = new Color(0.025f, 0.035f, 0.055f);
    [Tooltip("Con la foto: esconde el piso de la escena para que se vea el de la foto (alfombra, reflejo de la " +
             "puerta). Las criaturas pierden la sombra en el piso.")]
    public bool fotoSinPiso;

    [Header("Grupos (los arma el menú)")]
    public GameObject ventanal;
    public GameObject sigilo;
    public GameObject habitacion;
    public GameObject ojos;
    public GameObject foto;
    public GameObject living;
    [Tooltip("El piso de la escena: Living lo reemplaza por el de madera.")]
    public Renderer pisoOriginal;
    [Tooltip("Al salir de Foto / Living se le devuelve la niebla.")]
    public BannerIluminacion iluminacion;

    private void OnEnable() => Aplicar();

    // Si se borra el objeto (o se cierra la escena), el piso y la niebla quedan como estaban.
    private void OnDisable() => Restaurar();

    // SetActive desde OnValidate hace que Unity se queje (SendMessage): se difiere un ciclo.
    private void OnValidate() => EditorApplication.delayCall += () => { if (this != null) Aplicar(); };

    public void Aplicar()
    {
        Prender(ventanal, variante == Variante.Ventanal);
        Prender(sigilo, variante == Variante.Sigilo);
        Prender(habitacion, variante == Variante.Habitacion);
        Prender(ojos, variante == Variante.Ojos || ojosEncima);
        Prender(foto, variante == Variante.Foto);
        Prender(living, variante == Variante.Living);

        if (pisoOriginal != null)
            pisoOriginal.enabled = !(variante == Variante.Living || variante == Variante.Foto && fotoSinPiso);
        bool cuarto = variante == Variante.Foto || variante == Variante.Living;
        if (cuarto && nieblaAzul) RenderSettings.fogColor = colorNieblaAzul;
        else if (iluminacion != null) iluminacion.Aplicar();
    }

    private void Restaurar()
    {
        if (pisoOriginal != null) pisoOriginal.enabled = true;
        if (iluminacion != null) iluminacion.Aplicar();
    }

    private static void Prender(GameObject go, bool prendido)
    {
        if (go != null && go.activeSelf != prendido) go.SetActive(prendido);
    }
}
#endif
