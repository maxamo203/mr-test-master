using UnityEngine;

// Ajuste de desarrollo aplicado en vivo por los directores de entidades.
// En una partida multijugador sólo el host modifica movimiento autoritativo.
public static class EntitySpeedSettings
{
    public const float MinMultiplier = 0.25f;
    public const float MaxMultiplier = 3f;

    private static float _multiplier = 1f;

    public static float Multiplier
    {
        get => _multiplier;
        set => _multiplier = Mathf.Clamp(value, MinMultiplier, MaxMultiplier);
    }
}
