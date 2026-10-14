using UnityEngine;

// Sound design generated in code at startup (no extra files): the layers that turn the stock hit
// samples into heavy, cinematic impacts, plus the loops and stings of the boss fight.
public static class AudioSynth
{
    const int SR = 44100;
    static bool made;
    public static AudioClip SubThump, StoneCrack, Rumble, Zap, GunTick, Blast, Rocket, Heartbeat, Taiko, SwarmLoop, WindLoop, FireLoop, Spirit;

    public static void Make()
    {
        if (made) return;
        made = true;
        var rng = new System.Random(7);
        float N() => (float)(rng.NextDouble() * 2 - 1);

        // a deep body blow: a sine sweeping 95 -> 38 Hz with a fast attack
        SubThump = Clip("SubThump", 0.55f, (t, i) =>
        {
            float f = Mathf.Lerp(95, 38, Mathf.Clamp01(t / 0.25f));
            phase += 2 * Mathf.PI * f / SR;
            return Mathf.Sin(phase) * Env(t, 0.004f, 0.42f) * 0.95f;
        });

        // stone breaking: bursts of crackle under a short high click
        float lp = 0;
        StoneCrack = Clip("StoneCrack", 0.7f, (t, i) =>
        {
            float grain = (rng.NextDouble() < 0.02 + 0.2 * Mathf.Exp(-t * 18)) ? N() : 0;
            lp += (N() - lp) * 0.35f;
            return (grain * 0.9f + lp * 0.35f) * Env(t, 0.001f, 0.55f);
        });

        // the tail of a big impact: low filtered noise
        float r1 = 0, r2 = 0;
        Rumble = Clip("Rumble", 2.2f, (t, i) =>
        {
            r1 += (N() - r1) * 0.02f;
            r2 += (r1 - r2) * 0.05f;
            return r2 * 9f * Env(t, 0.03f, 2.0f);
        });

        // lightning: a crackling buzz with a falling pitch
        Zap = Clip("Zap", 0.9f, (t, i) =>
        {
            float f = Mathf.Lerp(900, 140, Mathf.Clamp01(t / 0.7f));
            phase += 2 * Mathf.PI * f / SR;
            float saw = (phase / Mathf.PI) % 2 - 1;
            float crackle = rng.NextDouble() < 0.08 ? N() : 0;
            return (saw * 0.35f + crackle * 0.8f + N() * 0.15f) * Env(t, 0.002f, 0.8f);
        });

        // a gatling round: a tiny sharp crack
        GunTick = Clip("GunTick", 0.08f, (t, i) => (N() * 0.8f + Mathf.Sin(t * 2 * Mathf.PI * 1800) * 0.3f) * Env(t, 0.0005f, 0.06f));

        // an explosion: a noise burst with a low body
        float b1 = 0;
        Blast = Clip("Blast", 1.8f, (t, i) =>
        {
            b1 += (N() - b1) * Mathf.Lerp(0.6f, 0.04f, Mathf.Clamp01(t / 0.4f));
            phase += 2 * Mathf.PI * Mathf.Lerp(70, 30, Mathf.Clamp01(t)) / SR;
            return (b1 * 1.4f + Mathf.Sin(phase) * 0.6f) * Env(t, 0.002f, 1.6f);
        });

        // a rocket's hiss (looped while it flies)
        float h1 = 0;
        Rocket = Clip("Rocket", 1f, (t, i) => { h1 += (N() - h1) * 0.25f; return (N() * 0.3f + h1 * 0.5f) * 0.6f; }, loop: true);

        // lub-dub
        Heartbeat = Clip("Heartbeat", 0.85f, (t, i) =>
        {
            float a = Beat(t, 0f) + Beat(t, 0.18f) * 0.7f;
            return Mathf.Sin(2 * Mathf.PI * 52 * t) * a;
        });

        // a taiko hit: a pitched membrane plus a slap
        Taiko = Clip("Taiko", 1.1f, (t, i) =>
        {
            float f = Mathf.Lerp(120, 70, Mathf.Clamp01(t / 0.15f));
            phase += 2 * Mathf.PI * f / SR;
            return (Mathf.Sin(phase) * Env(t, 0.002f, 0.9f) + N() * Env(t, 0.001f, 0.04f) * 0.5f) * 0.9f;
        });

        // thousands of stones in the air: a dense rattle of tiny clicks over a whooshing bed (loop)
        float s1 = 0;
        SwarmLoop = Clip("SwarmLoop", 3f, (t, i) =>
        {
            s1 += (N() - s1) * 0.08f;
            float click = rng.NextDouble() < 0.012 ? N() * 0.8f : 0;
            float swell = 0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * t / 3f);
            return (s1 * 1.8f * swell + click) * 0.55f;
        }, loop: true);

        // wind through the temple (loop)
        float w1 = 0, w2 = 0;
        WindLoop = Clip("WindLoop", 6f, (t, i) =>
        {
            float gust = 0.55f + 0.45f * Mathf.Sin(2 * Mathf.PI * t / 6f) * Mathf.Sin(2 * Mathf.PI * t / 2f);
            w1 += (N() - w1) * (0.01f + 0.02f * gust);
            w2 += (w1 - w2) * 0.1f;
            return w2 * 7f * gust;
        }, loop: true);

        // a crackling fire (loop)
        float f1 = 0;
        FireLoop = Clip("FireLoop", 4f, (t, i) =>
        {
            f1 += (N() - f1) * 0.05f;
            float pop = rng.NextDouble() < 0.0015 ? N() * 1.2f : 0;
            return f1 * 0.9f + pop;
        }, loop: true);

        // the guardian: a rising, shimmering chord
        Spirit = Clip("Spirit", 3f, (t, i) =>
        {
            float a = Env(t, 0.9f, 2.8f);
            float v = 0;
            float[] fr = { 220, 277.2f, 329.6f, 440 };
            for (int k = 0; k < fr.Length; k++) v += Mathf.Sin(2 * Mathf.PI * fr[k] * t * (1 + 0.003f * Mathf.Sin(t * 5 + k))) * (0.3f - k * 0.05f);
            return v * a * 0.6f;
        });
    }

    static float phase;

    // attack / decay envelope
    static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / (decay * 0.3f));
    static float Beat(float t, float at) { float d = t - at; return d < 0 ? 0 : Mathf.Exp(-d * 28) * Mathf.Min(1, d * 400); }

    static AudioClip Clip(string name, float seconds, System.Func<float, int, float> f, bool loop = false)
    {
        phase = 0;
        int n = Mathf.RoundToInt(seconds * SR);
        var data = new float[n];
        float peak = 1e-4f;
        for (int i = 0; i < n; i++) { data[i] = f(i / (float)SR, i); peak = Mathf.Max(peak, Mathf.Abs(data[i])); }
        float g = 0.9f / peak;
        for (int i = 0; i < n; i++) data[i] *= g;
        if (loop)
        {
            // crossfade the ends so the loop is seamless
            int x = Mathf.Min(n / 4, SR / 4);
            for (int i = 0; i < x; i++)
            {
                float k = i / (float)x;
                data[i] = data[i] * k + data[n - x + i] * (1 - k);
            }
            System.Array.Resize(ref data, n - x);
            n -= x;
        }
        var c = AudioClip.Create(name, n, 1, SR, false);
        c.SetData(data, 0);
        return c;
    }
}
