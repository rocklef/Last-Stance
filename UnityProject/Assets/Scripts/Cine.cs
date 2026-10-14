using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Cutscene camera work: each shot is a camera move (dolly, crane, push-in) from one framing to
// another over real time, so slow motion never slows the camera. FightCamera.Cine applies it and
// the HUD adds letterbox bars and dialogue. Skippable cutscenes end early on Enter / click.
public static class Cine
{
    public static bool Skipping { get; private set; }
    static bool skippable;

    public static void Begin(bool canSkip)
    {
        Skipping = false;
        skippable = canSkip && !AutoCapture.Active && !Showcase.Active;
        if (GameManager.I) GameManager.I.hud.SkipHint(skippable);
    }

    public static void End()
    {
        Skipping = false;
        skippable = false;
        if (GameManager.I) { GameManager.I.hud.SkipHint(false); GameManager.I.hud.Dialogue("", Color.white, "", 0); }
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

    static void CheckSkip()
    {
        if (!skippable || Skipping) return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (mouse != null && mouse.leftButton.wasPressedThisFrame) || MobileControls.Tap)
            Skipping = true;
    }

    // a camera move from (p0, l0, f0) to (p1, l1, f1); each(t) runs every frame with the time so far
    public static IEnumerator Shot(float dur, Vector3 p0, Vector3 p1, Vector3 l0, Vector3 l1, float f0, float f1, System.Action<float> each = null)
    {
        for (float x = 0; x < dur && !Skipping; x += Clock.Dt)
        {
            float k = Ease(x / dur);
            FightCamera.Cine(Vector3.Lerp(p0, p1, k), Vector3.Lerp(l0, l1, k), Mathf.Lerp(f0, f1, k));
            each?.Invoke(x);
            CheckSkip();
            yield return null;
        }
    }

    // a shot that follows moving things
    public static IEnumerator Track(float dur, System.Func<float, Vector3> pos, System.Func<float, Vector3> look, float fov, System.Action<float> each = null)
    {
        for (float x = 0; x < dur && !Skipping; x += Clock.Dt)
        {
            FightCamera.Cine(pos(x), look(x), fov);
            each?.Invoke(x);
            CheckSkip();
            yield return null;
        }
    }
}
