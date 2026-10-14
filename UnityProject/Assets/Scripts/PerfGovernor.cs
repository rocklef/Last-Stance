using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

// Performance mode, the way console games hold their frame rate: the frame time is watched every
// frame and the 3D render resolution (upscaled with AMD FSR) drops a step when frames run over the
// 60 fps budget and climbs back when there is headroom, so the game stays at 60 even when the whole
// town, 12,000 shards and the titan are on screen. F3 shows the numbers.
//   -uncapped   no frame-rate cap (for measuring)      -nogovernor   fixed full resolution
[DefaultExecutionOrder(-80)]
public class PerfGovernor : MonoBehaviour
{
    public const float Target = 1f / 60f;
    public const int DetailLayer = 11;   // small props: not drawn past DetailDistance
    const float DetailDistance = 60f;
    const float MaxScale = 1f, Step = 0.05f;
    float MinScale = 0.67f;
    UniversalRenderPipelineAsset urp;
    float smooth = Target, cool, scale = 1;
    bool governor = true, overlay;
    GUIStyle style;
    int frames;
    float fpsT, fps, worst, worstShown;

    public static float Fps { get; private set; }
    float logT = 10f;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        governor = System.Array.IndexOf(args, "-nogovernor") < 0 && !Showcase.Active;
        if (System.Array.IndexOf(args, "-uncapped") >= 0) { Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0; }
        var cam = Camera.main;
        if (cam != null)
        {
            var d = new float[32];
            d[DetailLayer] = DetailDistance;
            cam.layerCullDistances = d;
            cam.layerCullSpherical = true;
        }
        urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.upscalingFilter = UpscalingFilterSelection.FSR;
            urp.fsrOverrideSharpness = true;
            urp.fsrSharpness = 0.85f;
            urp.renderScale = 1;
            if (Application.isMobilePlatform)
            {
                // phones: 60 fps target, lighter shadows, and the resolution may drop further
                MinScale = 0.5f;
                scale = 0.75f;
                urp.renderScale = scale;
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, 45f);
                urp.msaaSampleCount = 1;
            }
        }
        if (Application.isMobilePlatform)
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f3Key.wasPressedThisFrame) overlay = !overlay;
        float dt = Time.unscaledDeltaTime;
        frames++;
        fpsT += dt;
        worst = Mathf.Max(worst, dt);
        if (fpsT >= 0.5f) { fps = frames / fpsT; Fps = fps; worstShown = worst; frames = 0; fpsT = 0; worst = 0; }
        if (Application.isMobilePlatform && (logT -= dt) <= 0) { logT = 10f; Debug.Log($"[PERF] fps={fps:0} scale={scale:0.00} worst={worstShown * 1000:0}ms"); }
        if (!governor || urp == null || dt > 0.25f) return;   // ignore loading hitches
        smooth = Mathf.Lerp(smooth, dt, 0.08f);
        cool -= dt;
        if (cool > 0) return;
        if (smooth > Target * 1.06f && scale > MinScale) { scale = Mathf.Max(MinScale, scale - Step); cool = 0.4f; }
        else if (smooth < Target * 0.8f && scale < MaxScale) { scale = Mathf.Min(MaxScale, scale + Step); cool = 1.2f; }
        urp.renderScale = scale;
    }

    void OnGUI()
    {
        if (!overlay) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        int w = Screen.width, h = Screen.height;
        string txt = $"{fps:0} fps   {1000f / Mathf.Max(1, fps):0.0} ms   worst {worstShown * 1000f:0.0} ms\n" +
                     $"render {Mathf.RoundToInt(w * scale)}x{Mathf.RoundToInt(h * scale)} -> {w}x{h} (FSR)   governor {(governor ? "on" : "off")}";
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(21, h - 79, 900, 60), txt, style);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, h - 80, 900, 60), txt, style);
    }
}
