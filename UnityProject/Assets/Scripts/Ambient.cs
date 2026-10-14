using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Living courtyard: flickering fire in the braziers, glowing lanterns, embers and drifting cherry petals.
public class Ambient : MonoBehaviour
{
    public Material fireMat, petalMat;
    public Transform[] fires;       // brazier tops
    public Transform[] lanterns;    // stone lantern fireboxes

    readonly List<(Light l, float seed, float baseI)> flicker = new List<(Light, float, float)>();

    void Start()
    {
        foreach (var f in fires)
        {
            Fire(f, 1f);
            flicker.Add((PointLight(f.position + Vector3.up * 0.5f, new Color(1f, 0.55f, 0.2f), 3.2f, 7f, true), Random.value * 10, 3.2f));
        }
        foreach (var l in lanterns)
            flicker.Add((PointLight(l.position, new Color(1f, 0.6f, 0.3f), 1.3f, 4f, false), Random.value * 10, 1.3f));
        Petals();
    }

    Light PointLight(Vector3 p, Color c, float intensity, float range, bool shadows)
    {
        var go = new GameObject("FireLight");
        go.transform.SetParent(transform);
        go.transform.position = p;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    void Fire(Transform at, float scale)
    {
        var go = new GameObject("Fire");
        go.transform.SetParent(at, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f * scale, 0.55f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.8f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.gravityModifier = -0.15f;
        var em = ps.emission;
        em.rateOverTime = 45;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.28f * scale;
        sh.rotation = new Vector3(-90, 0, 0);
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0.1f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.6f), new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.5f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = fireMat;
        r.shadowCastingMode = ShadowCastingMode.Off;

        // embers
        var eg = new GameObject("Embers");
        eg.transform.SetParent(at, false);
        var e = eg.AddComponent<ParticleSystem>();
        var em2 = e.main;
        em2.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        em2.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        em2.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        em2.startColor = new Color(1f, 0.6f, 0.2f);
        em2.simulationSpace = ParticleSystemSimulationSpace.World;
        em2.gravityModifier = -0.05f;
        var ee = e.emission;
        ee.rateOverTime = 8;
        var esh = e.shape;
        esh.shapeType = ParticleSystemShapeType.Circle;
        esh.radius = 0.25f;
        esh.rotation = new Vector3(-90, 0, 0);
        var en = e.noise;
        en.enabled = true;
        en.strength = 0.8f;
        var ec = e.colorOverLifetime;
        ec.enabled = true;
        var eg2 = new Gradient();
        eg2.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        ec.color = eg2;
        var er = eg.GetComponent<ParticleSystemRenderer>();
        er.sharedMaterial = fireMat;
        er.shadowCastingMode = ShadowCastingMode.Off;
    }

    void Petals()
    {
        var go = new GameObject("Petals");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(0, 9, 0);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 14f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.82f), new Color(1f, 0.88f, 0.92f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;
        main.gravityModifier = 0.012f;
        main.prewarm = true;
        var em = ps.emission;
        em.rateOverTime = 45;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(32, 1, 32);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.35f, -0.6f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.4f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = petalMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    void Update()
    {
        float t = Time.time;
        foreach (var (l, seed, b) in flicker)
            l.intensity = b * (0.8f + 0.35f * Mathf.PerlinNoise(t * 7f, seed) + 0.1f * Mathf.Sin(t * 23f + seed));
    }
}
