using System.IO;
using UnityEngine;

// Test harness: `LastStance.exe -autocapture <dir>` plays an AI-vs-AI match, saves screenshots of the
// intro, the biggest impacts (captured during hit-stop), knockouts and victory, logs [AUTO] lines
// with hits and frame rate, and quits when the match ends.
public class AutoCapture : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static string Dir;

    float t, tPrev, fpsT, lastShot = -10;
    int frames, shots, hitShots;
    float koAt = -1, overAt = -1;
    int hits, heavyHits, knockdowns;
    int maxCombo;
    int airHits, blocks, parries, guardBreaks, evades, grabs, throws, throwBreaks, specialShots, maxJuggle;
    float pendingThrowShot = -1, pendingDown = -1, pendingUp = -1, maxGap;
    int downShots;
    // motion monitor: hips speed spikes = visible snaps / pops
    Fighter[] watched;
    Vector3[] lastHips;
    int snaps;
    float worstSpeed;
    // leg quality: planted-toe sliding and knees bending backwards
    Vector3[,] lastToe = new Vector3[2, 2];
    float skate, standTime;
    int kneeFlips;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-autocapture");
        if (i < 0 || i + 1 >= args.Length) return;
        Active = true;
        Dir = args[i + 1];
        Directory.CreateDirectory(Dir);
        var go = new GameObject("AutoCapture");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoCapture>();
    }

    void Start()
    {
        Fighter.Hit += (a, v, m, d, p) =>
        {
            hits++;
            if (m.heavy || m.knockdown) heavyHits++;
            maxCombo = Mathf.Max(maxCombo, a.combo);
            if (v.state == Fighter.State.Air) { airHits++; maxJuggle = Mathf.Max(maxJuggle, v.juggles + 1); }
            float gap = Vector3.Distance(new Vector3(a.Body.x, 0, a.Body.z), new Vector3(v.Body.x, 0, v.Body.z));
            maxGap = Mathf.Max(maxGap, gap);
            Debug.Log($"[AUTO] t={t:0.0} hit {a.displayName}->{v.displayName} {m.name} dmg={d} combo={a.combo} hp={v.health} gap={gap:0.00} limbToBody={Vector3.Distance(p, v.Body + Vector3.up * 1.1f):0.00}");
            bool worthIt = m.heavy || m.knockdown || a.combo >= 3 || v.health <= 0;
            if (worthIt && hitShots < 10 && Time.unscaledTime - lastShot > 1.6f) { Shot($"hit_{m.name.Replace(' ', '_')}_{a.displayName}"); hitShots++; }
        };
        Fighter.Blocked += (a, v, m, p) => { blocks++; };
        Fighter.Parry += (a, v, p) => { parries++; Debug.Log($"[AUTO] t={t:0.0} PARRY {v.displayName} parried {a.displayName}"); if (specialShots < 6 && Time.unscaledTime - lastShot > 1.2f) { Shot($"parry_{v.displayName}"); specialShots++; } };
        Fighter.GuardBroken += f => { guardBreaks++; Debug.Log($"[AUTO] t={t:0.0} GUARD BREAK {f.displayName}"); if (specialShots < 6 && Time.unscaledTime - lastShot > 1.2f) { Shot($"guardbreak_{f.displayName}"); specialShots++; } };
        Fighter.Evaded += (a, d) => { evades++; Debug.Log($"[AUTO] t={t:0.0} EVADE {d.displayName}"); };
        Fighter.Grabbed += (a, v) => { grabs++; Debug.Log($"[AUTO] t={t:0.0} GRAB {a.displayName}->{v.displayName}"); if (specialShots < 6 && Time.unscaledTime - lastShot > 1.2f) { pendingThrowShot = t + 0.35f; } };
        Fighter.ThrowLanded += (a, v, d) => { throws++; Debug.Log($"[AUTO] t={t:0.0} THROW {a.displayName}->{v.displayName} dmg={d}"); };
        Fighter.ThrowBroken += (a, b) => { throwBreaks++; Debug.Log($"[AUTO] t={t:0.0} THROW BREAK"); };
        Fighter.Knocked += f =>
        {
            knockdowns++;
            Debug.Log($"[AUTO] t={t:0.0} knockdown {f.displayName}");
            if (downShots < 3) { pendingDown = t + 0.55f; pendingUp = t + 1.35f; downShots++; }
        };
        Fighter.KnockedOut += f => { koAt = t; Debug.Log($"[AUTO] t={t:0.0} KO {f.displayName}"); };
        GameManager.Log += s =>
        {
            Debug.Log($"[AUTO] t={t:0.0} {s}");
            if (s.StartsWith("match over")) overAt = t;
        };
    }

    void Shot(string name)
    {
        lastShot = Time.unscaledTime;
        var path = Path.Combine(Dir, $"{shots++:00}_{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[AUTO] shot {path}");
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        tPrev = t;
        t += dt;
        MonitorMotion();
        frames++;
        fpsT += dt;
        if (fpsT >= 5)
        {
            Debug.Log($"[AUTO] t={t:0.0} fps={frames / fpsT:0}");
            frames = 0;
            fpsT = 0;
        }
        if (pendingDown > 0 && Crossed(pendingDown)) { Shot("lying"); pendingDown = -1; }
        if (pendingUp > 0 && Crossed(pendingUp)) { Shot("getting_up"); pendingUp = -1; }
        if (pendingThrowShot > 0 && Crossed(pendingThrowShot)) { Shot("throw"); specialShots++; pendingThrowShot = -1; }
        if (Crossed(0.9f)) Shot("intro_crane");
        if (Crossed(2.6f)) Shot("round_announce");
        if (Crossed(7f)) Shot("fight_wide");
        if (Crossed(12f)) Shot("fight_mid");
        if (koAt > 0 && Crossed((koAt + 0.7f))) Shot("ko_slowmo");
        if (koAt > 0 && Crossed((koAt + 3.4f))) Shot("victory");
        if (overAt > 0 && Crossed((overAt + 1.5f))) Shot("match_over");
        if (t > 260 || (overAt > 0 && t > overAt + 3) || (GameManager.I == null && t > 5))
        {
            Debug.Log($"[AUTO] summary hits={hits} heavy={heavyHits} knockdowns={knockdowns} maxCombo={maxCombo} maxGap={maxGap:0.00} snaps={snaps} skatePerMin={(standTime > 0 ? skate / standTime * 60 : 0):0.0}m kneeFlipFrames={kneeFlips} worstHipSpeed={worstSpeed:0.0} maxJuggle={maxJuggle} airHits={airHits} blocks={blocks} parries={parries} guardBreaks={guardBreaks} evades={evades} grabs={grabs} throws={throws} throwBreaks={throwBreaks}");
            Application.Quit();
        }
    }

    void MonitorMotion()
    {
        if (GameManager.I == null) return;
        if (watched == null) { watched = new[] { GameManager.I.p1, GameManager.I.p2 }; lastHips = new Vector3[2]; for (int i = 0; i < 2; i++) lastHips[i] = Hips(watched[i]); return; }
        float sdt = Time.deltaTime;
        for (int i = 0; i < 2; i++)
        {
            var f = watched[i];
            var h = Hips(f);
            if (sdt > 0.001f && Time.timeScale > 0.5f)
            {
                float speed = Vector3.Distance(h, lastHips[i]) / sdt;
                bool fast = f.state == Fighter.State.Air || f.state == Fighter.State.Dash || f.state == Fighter.State.Intro;
                if (!fast)
                {
                    worstSpeed = Mathf.Max(worstSpeed, speed);
                    if (speed > 9f) { snaps++; if (snaps <= 12) Debug.Log($"[AUTO] t={t:0.0} SNAP {f.displayName} state={f.state} clip={f.anim.Current} speed={speed:0.0}m/s"); }
                }
            }
            lastHips[i] = h;
            MonitorLegs(i, f, sdt);
        }
    }

    void MonitorLegs(int i, Fighter f, float sdt)
    {
        bool standing = f.state == Fighter.State.Neutral || f.state == Fighter.State.Attack || f.state == Fighter.State.Hitstun || f.state == Fighter.State.Block;
        var a = f.animator;
        for (int s = 0; s < 2; s++)
        {
            var toe = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            var hip = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var knee = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var ankle = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (toe == null) continue;
            var tp = toe.position;
            if (standing && sdt > 0.001f && Time.timeScale > 0.5f && tp.y < 0.06f && lastToe[i, s].y < 0.06f)
                skate += new Vector3(tp.x - lastToe[i, s].x, 0, tp.z - lastToe[i, s].z).magnitude;
            lastToe[i, s] = tp;
            if (standing)
            {
                // knee in front of the hip-ankle line = normal; well behind it = bent backwards
                Vector3 mid = (hip.position + ankle.position) * 0.5f;
                float fwd = Vector3.Dot(knee.position - mid, f.transform.forward);
                if (fwd < -0.06f) kneeFlips++;
            }
        }
        if (standing && Time.timeScale > 0.5f) standTime += sdt;
    }

    static Vector3 Hips(Fighter f)
    {
        if (f == null || f.animator == null) return Vector3.zero;
        var hips = f.animator.GetBoneTransform(HumanBodyBones.Hips);
        return hips != null ? hips.position : f.transform.position;
    }

    bool Crossed(float x) => tPrev < x && t >= x;

    void OnApplicationQuit() => Debug.Log($"[AUTO] quit t={t:0.0} hits={hits} heavy={heavyHits} knockdowns={knockdowns} maxCombo={maxCombo}");
}
