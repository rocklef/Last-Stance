using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Global time control: hit-stop freezes briefly, slow motion for knockouts.
public static class TimeFX
{
    static float stopUntil, slowUntil, slowScale = 1;
    public static float slow = 1f;
    public static bool paused;

    public static void HitStop(float d) => stopUntil = Mathf.Max(stopUntil, Clock.Now + d);
    public static bool Stopped => Clock.Now < stopUntil;

    // Brief slow motion (parries, evades) that eases back to normal speed.
    public static void SlowFor(float scale, float seconds)
    {
        slowScale = scale;
        slowUntil = Clock.Now + seconds;
    }

    public static void Tick()
    {
        float s = slow;
        float left = slowUntil - Clock.Now;
        if (left > 0) s = Mathf.Min(s, Mathf.Lerp(1f, slowScale, Mathf.Clamp01(left / 0.12f)));
        Time.timeScale = paused ? 0f : Stopped ? 0.02f : s;
    }
}

// Impact effects and sound: red hit splashes, sparks, shockwave rings, floor stains, dust,
// hit-stop, camera shake and a chromatic-aberration pulse on heavy hits.
public class FX : MonoBehaviour
{
    public Material bloodMat, sparkMat, ringMat, dustMat, decalMat;
    public AudioClip hitLight, hitHeavy, whooshLight, whooshHeavy, bodyFall, gong, koBoom, music, block, parry;
    public VolumeProfile volume;

    ParticleSystem blood, mist, sparks, rings, dust;
    readonly List<(Transform t, Material m, float born, float life)> decals = new List<(Transform, Material, float, float)>();
    int decalNext;
    AudioSource[] voices;
    int voice;
    AudioSource musicSource;
    ChromaticAberration chroma;
    float chromaPulse;
    Transform floorPlane;

    public static FX I;

    void Awake()
    {
        I = this;
        floorPlane = new GameObject("FXFloorPlane").transform;
        floorPlane.SetParent(transform);
        floorPlane.position = new Vector3(0, 0.01f, 0);

        blood = MakePS("Blood", bloodMat, 3000, gravity: 1.7f, stretch: true, collide: true);
        mist = MakePS("BloodMist", bloodMat, 400, gravity: 0.15f);
        sparks = MakePS("Sparks", sparkMat, 800, gravity: 0.4f, stretch: true);
        rings = MakePS("Rings", ringMat, 30, gravity: 0, growRing: true);
        dust = MakePS("Dust", dustMat, 400, gravity: -0.05f, grow: true);

        voices = new AudioSource[8];
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = gameObject.AddComponent<AudioSource>();
            voices[i].playOnAwake = false;
            voices[i].spatialBlend = 0;
        }
        var mgo = new GameObject("Music");
        mgo.transform.SetParent(transform);
        musicSource = mgo.AddComponent<AudioSource>();
        musicSource.clip = music;
        musicSource.loop = true;
        musicSource.volume = 0.42f;
        musicSource.playOnAwake = false;
        if (music) musicSource.Play();

        if (volume) volume.TryGet(out chroma);

        Fighter.Hit += OnHit;
        Fighter.Swing += OnSwing;
        Fighter.Knocked += OnKnocked;
        Fighter.KnockedOut += OnKO;
        Fighter.Blocked += OnBlocked;
        Fighter.Parry += OnParry;
        Fighter.GuardBroken += OnGuardBreak;
        Fighter.Evaded += OnEvaded;
        Fighter.Grabbed += OnGrabbed;
        Fighter.ThrowLanded += OnThrowLanded;
        Fighter.ThrowBroken += OnThrowBroken;
    }

    void OnDestroy()
    {
        Fighter.Hit -= OnHit;
        Fighter.Swing -= OnSwing;
        Fighter.Knocked -= OnKnocked;
        Fighter.KnockedOut -= OnKO;
        Fighter.Blocked -= OnBlocked;
        Fighter.Parry -= OnParry;
        Fighter.GuardBroken -= OnGuardBreak;
        Fighter.Evaded -= OnEvaded;
        Fighter.Grabbed -= OnGrabbed;
        Fighter.ThrowLanded -= OnThrowLanded;
        Fighter.ThrowBroken -= OnThrowBroken;
        if (chroma) chroma.intensity.value = 0;
    }

    ParticleSystem MakePS(string name, Material mat, int max, float gravity, bool stretch = false, bool collide = false, bool grow = false, bool growRing = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var em = ps.emission;
        em.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = g;

        if (grow || growRing)
        {
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, growRing ? AnimationCurve.EaseInOut(0, 0.15f, 1, 1) : AnimationCurve.Linear(0, 0.5f, 1, 1.6f));
        }
        if (collide)
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.Planes;
            c.SetPlane(0, floorPlane);
            c.dampen = 0.92f;
            c.bounce = 0.05f;
            c.lifetimeLoss = 0f;
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch) { r.velocityScale = 0.035f; r.lengthScale = 1.2f; }
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    // ------------------------------------------------------------------ events
    void OnHit(Fighter attacker, Fighter victim, Move m, float dmg, Vector3 p)
    {
        bool big = m.heavy || m.knockdown || victim.health <= 0;
        Vector3 dir = (victim.transform.position - attacker.transform.position);
        dir.y = 0;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : attacker.transform.forward;

        Splash(p, dir, big ? 1f : 0.45f);
        Spark(p, dir, big);
        if (big) Ring(p, victim.health <= 0 ? 2.6f : 1.8f);
        Stains(p, dir, big ? Random.Range(2, 4) : Random.value < 0.5f ? 1 : 0);

        float stop = m.hitstop * (victim.health <= 0 ? 2.2f : 1f);
        TimeFX.HitStop(stop);
        FightCamera.Shake(m.shake * (victim.health <= 0 ? 1.8f : 1f));
        if (big) FightCamera.Punch(victim.health <= 0 ? 1f : 0.55f);
        if (big) chromaPulse = victim.health <= 0 ? 1f : 0.6f;
        Play(big ? hitHeavy : hitLight, big ? 1f : 0.85f, Random.Range(0.9f, 1.1f));
        if (big) SoundStage.At(AudioSynth.SubThump, p, victim.health <= 0 ? 1f : 0.6f, 1.1f);
    }

    void OnSwing(Fighter f, Move m) => Play(m.heavy || m.limb == Limb.FootR ? whooshHeavy : whooshLight, 0.45f, Random.Range(0.92f, 1.12f) / Mathf.Max(0.5f, f.attackSpeed) * (f.isBrute ? 0.85f : 1f));

    void OnKnocked(Fighter f)
    {
        Dust(f.transform.position + f.transform.forward * -0.4f);
        FightCamera.Shake(0.12f);
        Play(bodyFall, 0.9f, Random.Range(0.9f, 1.05f));
    }

    void OnKO(Fighter f) => Play(koBoom, 1f, 1f);

    void OnBlocked(Fighter attacker, Fighter defender, Move m, Vector3 p)
    {
        Vector3 dir = (defender.transform.position - attacker.transform.position).WithY(0).normalized;
        Burst(sparks, p, dir, m.heavy ? 18 : 9, new Color(0.7f, 0.85f, 1f), new Color(1f, 1f, 1f), 3f, 7f);
        TimeFX.HitStop(m.hitstop * 0.6f);
        FightCamera.Shake(m.shake * 0.4f);
        Play(block ? block : hitLight, 0.8f, Random.Range(0.95f, 1.1f) * (m.heavy ? 0.85f : 1f));
    }

    void OnParry(Fighter attacker, Fighter defender, Vector3 p)
    {
        Vector3 dir = (attacker.transform.position - defender.transform.position).WithY(0).normalized;
        Burst(sparks, p, dir, 40, new Color(0.6f, 0.9f, 1f), Color.white, 5f, 12f);
        Ring(p, 2.2f);
        TimeFX.HitStop(0.12f);
        TimeFX.SlowFor(0.3f, 0.55f);
        FightCamera.Shake(0.2f);
        FightCamera.Punch(0.7f);
        chromaPulse = 0.8f;
        Play(parry ? parry : hitHeavy, 1f, 1f);
    }

    void OnGuardBreak(Fighter f)
    {
        var p = f.transform.position + Vector3.up * 1.3f;
        Burst(sparks, p, -f.transform.forward, 36, new Color(1f, 0.75f, 0.3f), Color.white, 4f, 10f);
        Ring(p, 2f);
        TimeFX.HitStop(0.14f);
        FightCamera.Shake(0.3f);
        FightCamera.Punch(0.6f);
        Play(hitHeavy, 1f, 0.8f);
    }

    void OnEvaded(Fighter attacker, Fighter dodger)
    {
        TimeFX.SlowFor(0.25f, 0.6f);
        chromaPulse = 0.5f;
        Play(whooshHeavy, 0.7f, 0.7f);
    }

    void OnGrabbed(Fighter thrower, Fighter victim)
    {
        FightCamera.Punch(0.4f);
        Play(block ? block : hitLight, 0.7f, 0.8f);
    }

    void OnThrowLanded(Fighter thrower, Fighter victim, float dmg)
    {
        var p = victim.transform.position + Vector3.up * 1.1f;
        Splash(p, thrower.transform.forward, 0.6f);
        TimeFX.HitStop(0.1f);
        FightCamera.Shake(0.25f);
        FightCamera.Punch(0.5f);
        Play(hitHeavy, 1f, 0.9f);
    }

    void OnThrowBroken(Fighter a, Fighter b)
    {
        var p = (a.transform.position + b.transform.position) * 0.5f + Vector3.up * 1.2f;
        Burst(sparks, p, Vector3.up, 24, Color.white, new Color(0.8f, 0.9f, 1f), 3f, 8f);
        Ring(p, 1.6f);
        TimeFX.HitStop(0.08f);
        Play(parry ? parry : block, 0.8f, 1.2f);
    }

    void Burst(ParticleSystem ps, Vector3 p, Vector3 dir, int n, Color a, Color b, float vMin, float vMax)
    {
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            e.position = p;
            e.velocity = (dir + Random.insideUnitSphere * 1.3f).normalized * Random.Range(vMin, vMax);
            e.startSize = Random.Range(0.015f, 0.04f);
            e.startLifetime = Random.Range(0.12f, 0.3f);
            e.startColor = Color.Lerp(a, b, Random.value);
            ps.Emit(e, 1);
        }
        e.position = p;
        e.velocity = Vector3.zero;
        e.startSize = 0.7f;
        e.startLifetime = 0.08f;
        e.startColor = b;
        ps.Emit(e, 1);
    }

    public void Gong() => Play(gong, 0.8f, 1f);

    // Boss-scale impacts (Moros's weapons): shockwave ring, dust, sparks, a chromatic pulse and a
    // deep boom.
    public void Impact(Vector3 p, float size, float pitch = 0.8f)
    {
        Ring(new Vector3(p.x, 0.05f, p.z), size);
        Dust(new Vector3(p.x, 0, p.z));
        Spark(p, Vector3.up, true);
        chromaPulse = Mathf.Max(chromaPulse, Mathf.Clamp01(size / 6f));
        Play(koBoom ? koBoom : hitHeavy, Mathf.Clamp01(0.45f + size * 0.08f), pitch);
        SoundStage.Impact(p, size);
    }

    public void Whoosh(float pitch, float vol = 0.8f) => Play(whooshHeavy, vol, pitch);
    public void Chroma(float v) => chromaPulse = Mathf.Max(chromaPulse, v);
    public void Shock(Vector3 p, float size) => Ring(new Vector3(p.x, 0.05f, p.z), size);
    public void Crack(Vector3 p, float pitch) { Spark(p, Vector3.up, true); Play(hitHeavy, 0.9f, pitch); SoundStage.At(AudioSynth.StoneCrack, p, 0.6f, pitch); }

    public void DuckMusic(float v) { MusicDuck = v / 0.42f; if (musicSource) musicSource.volume = v; }
    public float MusicDuck { get; private set; } = 1;
    public AudioSource Music => musicSource;

    // ------------------------------------------------------------------ hero powers / Moros's arsenal
    // a coloured shockwave ring at any height (Thunder Palm, the Guardian's fists, explosions)
    public void Pulse(Vector3 p, float size, Color c, float life = 0.35f)
    {
        var e = new ParticleSystem.EmitParams { position = p, velocity = Vector3.zero, startSize = size, startLifetime = life, startColor = c };
        rings.Emit(e, 1);
    }

    // a burst of coloured sparks (lightning crackle, blue slashes, gunfire)
    public void Energy(Vector3 p, Vector3 dir, int n, Color a, Color b, float vMin, float vMax) => Burst(sparks, p, dir, n, a, b, vMin, vMax);

    // fire / rocket exhaust: fat, slow, glowing embers
    public void Embers(Vector3 p, Vector3 v, int n, float size = 0.18f)
    {
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            e.position = p + Random.insideUnitSphere * size;
            e.velocity = v + Random.insideUnitSphere * (v.magnitude * 0.25f + 1f);
            e.startSize = Random.Range(size * 0.6f, size * 1.6f);
            e.startLifetime = Random.Range(0.25f, 0.6f);
            e.startColor = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.3f, 0.08f), Random.value);
            sparks.Emit(e, 1);
        }
    }

    // a puff of dust / smoke
    public void Puff(Vector3 p, float size = 0.6f)
    {
        var e = new ParticleSystem.EmitParams { position = p, velocity = Random.insideUnitSphere * 0.6f + Vector3.up * 0.5f, startSize = size, startLifetime = 0.9f, startColor = new Color(0.7f, 0.66f, 0.62f, 0.4f) };
        dust.Emit(e, 1);
    }

    // ------------------------------------------------------------------ emitters
    public void Splash(Vector3 p, Vector3 dir, float power)
    {
        var e = new ParticleSystem.EmitParams();
        int n = Mathf.RoundToInt(Mathf.Lerp(14, 55, power));
        for (int i = 0; i < n; i++)
        {
            Vector3 v = (dir * Random.Range(1.5f, 4.5f) + Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(0.5f, 2.5f)) * Mathf.Lerp(0.8f, 1.3f, power);
            e.position = p + Random.insideUnitSphere * 0.04f;
            e.velocity = v;
            e.startSize = Random.Range(0.018f, 0.055f) * (Random.value < 0.15f ? 2f : 1f);
            e.startLifetime = Random.Range(0.7f, 1.6f);
            e.startColor = Color.Lerp(new Color(0.55f, 0.02f, 0.02f), new Color(0.85f, 0.05f, 0.04f), Random.value);
            blood.Emit(e, 1);
        }
        int puffs = Mathf.RoundToInt(Mathf.Lerp(3, 8, power));
        for (int i = 0; i < puffs; i++)
        {
            e.position = p + dir * 0.05f;
            e.velocity = dir * Random.Range(0.4f, 1.3f) + Random.insideUnitSphere * 0.5f;
            e.startSize = Random.Range(0.12f, 0.3f) * Mathf.Lerp(0.8f, 1.4f, power);
            e.startLifetime = Random.Range(0.2f, 0.4f);
            e.startColor = new Color(0.7f, 0.03f, 0.03f, 0.55f);
            mist.Emit(e, 1);
        }
    }

    void Spark(Vector3 p, Vector3 dir, bool big)
    {
        var e = new ParticleSystem.EmitParams();
        int n = big ? 26 : 10;
        for (int i = 0; i < n; i++)
        {
            e.position = p;
            e.velocity = (dir + Random.insideUnitSphere * 1.1f).normalized * Random.Range(4f, big ? 11f : 7f);
            e.startSize = Random.Range(0.015f, 0.035f);
            e.startLifetime = Random.Range(0.1f, 0.25f);
            e.startColor = Color.Lerp(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.45f, 0.2f), Random.value);
            sparks.Emit(e, 1);
        }
        e.position = p;
        e.velocity = Vector3.zero;
        e.startSize = big ? 0.9f : 0.5f;
        e.startLifetime = 0.08f;
        e.startColor = new Color(1f, 0.85f, 0.7f, 1f);
        sparks.Emit(e, 1);
    }

    void Ring(Vector3 p, float size)
    {
        var e = new ParticleSystem.EmitParams { position = p, velocity = Vector3.zero, startSize = size, startLifetime = 0.28f, startColor = new Color(1f, 0.9f, 0.8f, 0.9f) };
        rings.Emit(e, 1);
    }

    void Dust(Vector3 p)
    {
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < 26; i++)
        {
            float a = i / 26f * Mathf.PI * 2;
            Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            e.position = p + d * 0.3f + Vector3.up * 0.1f;
            e.velocity = d * Random.Range(1.2f, 2.8f) + Vector3.up * Random.Range(0.1f, 0.6f);
            e.startSize = Random.Range(0.35f, 0.7f);
            e.startLifetime = Random.Range(0.6f, 1.1f);
            e.startColor = new Color(0.75f, 0.68f, 0.6f, 0.35f);
            dust.Emit(e, 1);
        }
    }

    // Flat red stains on the stone floor that fade out over several seconds.
    void Stains(Vector3 p, Vector3 dir, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Transform t;
            Material m;
            if (decals.Count < 48)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "Stain";
                q.transform.SetParent(transform);
                var r = q.GetComponent<MeshRenderer>();
                m = new Material(decalMat);
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                t = q.transform;
                decals.Add((t, m, 0, 0));
            }
            int idx = decals.Count < 48 ? decals.Count - 1 : decalNext++ % 48;
            (t, m, _, _) = decals[idx];
            Vector3 at = new Vector3(p.x, 0, p.z) + dir * Random.Range(0.3f, 1.3f) + Random.insideUnitSphere.WithY(0) * 0.35f;
            t.position = at + Vector3.up * (0.006f + idx * 0.0002f);
            t.rotation = Quaternion.Euler(90, Random.Range(0, 360f), 0);
            float s = Random.Range(0.18f, 0.5f);
            t.localScale = new Vector3(s, s * Random.Range(0.6f, 1.2f), 1);
            t.gameObject.SetActive(true);
            decals[idx] = (t, m, Time.time, Random.Range(7f, 11f));
        }
    }

    void Update()
    {
        for (int i = 0; i < decals.Count; i++)
        {
            var d = decals[i];
            if (!d.t.gameObject.activeSelf) continue;
            float age = Time.time - d.born;
            float a = Mathf.Clamp01(1 - (age - d.life * 0.6f) / (d.life * 0.4f));
            d.m.SetColor("_BaseColor", new Color(0.35f, 0.01f, 0.01f, 0.85f * a));
            if (age > d.life) d.t.gameObject.SetActive(false);
        }
        chromaPulse = Mathf.MoveTowards(chromaPulse, 0, Clock.Dt * 2.5f);
        if (chroma) chroma.intensity.value = chromaPulse;
    }

    public static System.Action<AudioClip, float, float> Played;   // showcase: log every sound for the video mix
    public void Play(AudioClip c, float vol, float pitch)
    {
        if (!c) return;
        Played?.Invoke(c, vol, pitch);
        var s = voices[voice++ % voices.Length];
        s.pitch = pitch;
        s.PlayOneShot(c, vol);
    }
}

static class VecExt
{
    public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
}
