using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays mocap clips on a humanoid, driven entirely from code, with inertialization instead of
// cross-fades: a new clip takes over instantly and the difference between the old pose and the new
// one is carried as a per-bone offset that decays smoothly to zero. Unlike a cross-fade (which
// averages two poses and makes bodies sag and bounce) the motion keeps its momentum and never
// snaps. Clips advance on scaled game time, so hit-stop and slow motion freeze them too.
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Animator))]
public class AnimDriver : MonoBehaviour
{
    public AnimLibrary library;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable cur, prev;
    int curSlot;
    float fade, fadeDur;
    string curName;
    bool curLoop;
    float curSpeed;

    // inertialization state
    Transform[] bones;
    Quaternion[] last, offset;
    Vector3 lastHip, hipOffset;
    bool hasLast, inertPending;
    float inertT, inertDur;

    public string Current => curName;
    // frame a clip was (re)started: root motion on that frame is a bogus jump from time 0 to the start offset
    public int StartFrame { get; private set; } = -10;
    public float Time => cur.IsValid() ? (float)cur.GetTime() : 0;
    public float Length => cur.IsValid() ? cur.GetAnimationClip().length : 0;
    public bool Finished => cur.IsValid() && !curLoop && Time >= Length - 0.001f;

    void Awake()
    {
        var anim = GetComponent<Animator>();
        anim.runtimeAnimatorController = null;
        anim.applyRootMotion = true;
        graph = PlayableGraph.Create(name + "Anim");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        var output = AnimationPlayableOutput.Create(graph, "out", anim);
        output.SetSourcePlayable(mixer);
        graph.Play();

        var list = new System.Collections.Generic.List<Transform>();
        for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
        {
            var t = anim.GetBoneTransform(b);
            if (t != null) list.Add(t);
        }
        bones = list.ToArray();
        last = new Quaternion[bones.Length];
        offset = new Quaternion[bones.Length];
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    public bool Has(string clip) => library[clip] != null;

    static readonly System.Collections.Generic.HashSet<string> NoFootIK = new System.Collections.Generic.HashSet<string>
        { "FallingBack", "GettingUp", "KnockedOut", "BigUppercutReact" };

    // Starts a clip (restarting it if it is already playing unless keepIfSame).
    public void Play(string clip, float fadeTime, float start = 0, float speed = 1, bool loop = false, bool keepIfSame = false)
    {
        if (keepIfSame && clip == curName && cur.IsValid()) { SetSpeed(speed); return; }
        var e = library[clip];
        if (e == null) { Debug.LogWarning("AnimDriver: no clip " + clip); return; }

        // drop the clip that was already fading out; the current one becomes the fade-out source
        if (prev.IsValid())
        {
            graph.Disconnect(mixer, 1 - curSlot);
            prev.Destroy();
            prev = default;
        }
        int slot = curSlot;
        if (cur.IsValid())
        {
            prev = cur;
            slot = 1 - curSlot;
        }

        cur = AnimationClipPlayable.Create(graph, e.clip);
        StartFrame = UnityEngine.Time.frameCount;
        // Unity's clip foot IK keeps retargeted feet where the mocap put them (without it knees
        // twist and feet skate); it fights the body on the floor, so lying/falling clips skip it
        cur.SetApplyFootIK(!NoFootIK.Contains(clip));
        cur.SetTime(start);
        cur.SetTime(start);   // twice: avoids a root-motion jump from time 0
        cur.SetSpeed(speed);
        graph.Connect(cur, 0, mixer, slot);
        curSlot = slot;
        curName = clip;
        curLoop = loop;
        curSpeed = speed;
        fadeDur = 0.0001f;
        fade = 0;
        Weights();
        if (hasLast)
        {
            inertPending = true;
            inertDur = Mathf.Clamp(fadeTime * 2.2f, 0.08f, 0.35f);
        }
    }

    // Freezes the current clip at a time (for poses such as holding a guard or a lying body).
    public void Hold(string clip, float t, float fadeTime)
    {
        if (clip != curName) Play(clip, fadeTime, t, 0);
        cur.SetSpeed(0);
        cur.SetTime(t);
    }

    public void SetSpeed(float s)
    {
        curSpeed = s;
        if (cur.IsValid()) cur.SetSpeed(s);
    }

    public void SetTime(float t)
    {
        if (cur.IsValid()) cur.SetTime(t);
    }

    void Update()
    {
        if (!cur.IsValid()) return;
        float t = (float)cur.GetTime();
        float len = cur.GetAnimationClip().length;
        if (curLoop && t > len) cur.SetTime(t % len);
        else if (!curLoop && t > len) { cur.SetTime(len); cur.SetSpeed(0); }

        if (fade < fadeDur)
        {
            fade += UnityEngine.Time.deltaTime;
            Weights();
        }
    }

    // Runs after the animator has written this frame's pose.
    void LateUpdate()
    {
        if (bones == null || bones.Length == 0) return;
        var hips = bones[0];
        if (inertPending)
        {
            inertPending = false;
            inertT = 0;
            for (int i = 0; i < bones.Length; i++) offset[i] = last[i] * Quaternion.Inverse(bones[i].localRotation);
            hipOffset = lastHip - hips.localPosition;
        }
        if (inertT < inertDur)
        {
            inertT += UnityEngine.Time.deltaTime;
            // quintic ease-out of the leftover: zero velocity at the end, no pop
            float x = Mathf.Clamp01(inertT / inertDur);
            float w = 1 - x * x * x * (x * (x * 6 - 15) + 10);
            for (int i = 0; i < bones.Length; i++)
                bones[i].localRotation = Quaternion.Slerp(Quaternion.identity, offset[i], w) * bones[i].localRotation;
            hips.localPosition += hipOffset * w;
        }
        for (int i = 0; i < bones.Length; i++) last[i] = bones[i].localRotation;
        lastHip = hips.localPosition;
        hasLast = true;
    }

    void Weights()
    {
        float k = fadeDur <= 0.0001f ? 1 : Mathf.Clamp01(fade / fadeDur);
        k = k * k * (3 - 2 * k);
        mixer.SetInputWeight(curSlot, k);
        mixer.SetInputWeight(1 - curSlot, prev.IsValid() ? 1 - k : 0);
    }
}
