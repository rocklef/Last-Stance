using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Drives the atmosphere pass (Shaders/Atmosphere.shader): height fog that glows toward the low
// sunset sun and god rays streaming around the roofs, plus a cinematic depth of field that the
// hero powers pull focus with (Atmosphere.Focus).
[DefaultExecutionOrder(200)]
public class Atmosphere : MonoBehaviour
{
    [Header("Haze")]
    public Color hazeColor = new Color(0.7f, 0.5f, 0.44f);
    public float density = 0.009f, baseHeight = 0f, falloff = 0.15f, maxOpacity = 0.6f, startDistance = 20f;
    [Header("Sun and god rays")]
    public Color sunColor = new Color(1f, 0.68f, 0.4f);
    public float sunIntensity = 2.2f, rayStrength = 0.85f, rayThreshold = 0.9f, rayDecay = 0.965f, rayLength = 0.9f;

    static Atmosphere inst;
    DepthOfField dof;
    float focusT, focusDur, aperture = 16;
    Vector3 focusPoint;
    System.Func<Vector3> focusTrack;

    void Awake() => inst = this;

    void Start()
    {
        foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal && v.sharedProfile != null && v.sharedProfile.TryGet(out dof)) break;
        if (dof != null) { dof.active = true; dof.mode.overrideState = true; dof.mode.value = DepthOfFieldMode.Off; }
    }

    // Pull focus onto a point (or something that moves) for a few seconds: the background melts
    // away behind a power's close-up.
    public static void Focus(System.Func<Vector3> target, float seconds)
    {
        if (inst == null) return;
        inst.focusTrack = target;
        inst.focusDur = seconds;
        inst.focusT = 0;
    }

    void LateUpdate()
    {
        var sun = RenderSettings.sun;
        Vector3 toSun = sun ? -sun.transform.forward : new Vector3(0.3f, 0.4f, 0.8f).normalized;
        Shader.SetGlobalVector("_AtmoSunDir", new Vector4(toSun.x, toSun.y, toSun.z, rayStrength));
        Shader.SetGlobalColor("_AtmoSunColor", sunColor * sunIntensity);
        Shader.SetGlobalVector("_AtmoFogColor", new Vector4(hazeColor.r, hazeColor.g, hazeColor.b, density));
        Shader.SetGlobalVector("_AtmoFogHeight", new Vector4(baseHeight, falloff, maxOpacity, startDistance));
        Shader.SetGlobalVector("_AtmoRays", new Vector4(rayThreshold, rayDecay, rayLength, 1));
        UpdateFocus();
    }

    void UpdateFocus()
    {
        if (dof == null) return;
        var cam = Camera.main;
        bool on = focusTrack != null && focusT < focusDur && cam != null;
        if (on)
        {
            focusT += Clock.Dt;
            focusPoint = focusTrack();
            // rack in, hold, rack out
            float want = focusT < focusDur - 0.4f ? 2.2f : 16f;
            aperture = Mathf.MoveTowards(aperture, want, Clock.Dt * 40f);
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.overrideState = true;
            dof.focusDistance.value = Mathf.Max(0.3f, Vector3.Distance(cam.transform.position, focusPoint));
            dof.aperture.overrideState = true;
            dof.aperture.value = aperture;
            dof.focalLength.overrideState = true;
            dof.focalLength.value = 55f;
        }
        else if (dof.mode.value != DepthOfFieldMode.Off)
        {
            dof.mode.value = DepthOfFieldMode.Off;
            focusTrack = null;
            aperture = 16;
        }
    }
}
