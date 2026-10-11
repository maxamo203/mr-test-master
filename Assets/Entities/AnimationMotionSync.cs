using UnityEngine;

// Curva comun para locomocion in-place: conserva la velocidad media configurada, pero
// concentra el avance entre apoyos/impulsos para que el cuerpo no se deslice por el piso.
public static class AnimationMotionSync
{
    public static float Evaluate(double playbackSeconds, float clipLength,
                                 int impulsesPerLoop, float plantedSpeedRatio)
    {
        if (clipLength <= 0.001f) return 1f;
        return EvaluateNormalized(
            (float)(playbackSeconds / clipLength), impulsesPerLoop, plantedSpeedRatio);
    }

    public static float EvaluateNormalized(float normalizedTime, int impulsesPerLoop,
                                           float plantedSpeedRatio)
    {
        if (impulsesPerLoop <= 0) return 1f;

        float loop = Mathf.Repeat(normalizedTime, 1f);
        // El primer apoyo queda a mitad del primer segmento; el avance máximo sucede
        // entre dos apoyos consecutivos.
        float impulsePhase = Mathf.Repeat(loop * impulsesPerLoop - 0.5f, 1f);
        float transfer = Mathf.Sin(Mathf.PI * impulsePhase);
        float planted = Mathf.Clamp01(plantedSpeedRatio);

        // mean(sin²) = 0.5, por eso el promedio del multiplicador sigue siendo 1.
        return planted + 2f * (1f - planted) * transfer * transfer;
    }
}
