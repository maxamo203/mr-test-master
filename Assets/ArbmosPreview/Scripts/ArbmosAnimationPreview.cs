using UnityEngine;

public sealed class ArbmosAnimationPreview : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private string currentState = "Idle 01 - Predatory Droop";

    private readonly string[] stateNames =
    {
        "Idle 01 - Predatory Droop",
        "Idle 03 - Broken Marionette",
        "Idle 05 - Cadaveric Stillness",
        "Chase - Inevitable Stalk"
    };

    public void Configure(Animator target)
    {
        animator = target;
    }

    private void Start()
    {
        PlayState(0);
    }

    private void PlayState(int index)
    {
        if (animator == null || index < 0 || index >= stateNames.Length)
            return;

        currentState = stateNames[index];
        animator.CrossFadeInFixedTime(currentState, 0.18f, 0, 0f);
    }

    private void OnGUI()
    {
        const float width = 300f;
        GUILayout.BeginArea(new Rect(20f, 20f, width, 260f), GUI.skin.box);
        GUILayout.Label("ARBMOS — ANIMATION LAB");
        GUILayout.Space(6f);
        GUILayout.Label("Playing: " + currentState);
        GUILayout.Space(10f);

        if (GUILayout.Button("1 — Predatory Droop", GUILayout.Height(38f))) PlayState(0);
        if (GUILayout.Button("3 — Broken Marionette", GUILayout.Height(38f))) PlayState(1);
        if (GUILayout.Button("5 — Cadaveric Stillness", GUILayout.Height(38f))) PlayState(2);
        if (GUILayout.Button("CHASE — Inevitable Stalk", GUILayout.Height(44f))) PlayState(3);

        GUILayout.EndArea();
    }
}
