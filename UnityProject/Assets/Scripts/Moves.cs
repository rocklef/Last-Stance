using System.Collections.Generic;
using UnityEngine;

public enum Limb { HandL, HandR, FootL, FootR, KneeR, KneeL, Head, ElbowL, ElbowR }
public enum Btn { Light, Heavy }

// One attack: keyframed poses over time, when it can hit, and what the hit does.
public class Move
{
    public string name;
    public (float t, Pose pose)[] keys;   // times in seconds at attack speed 1
    public float duration;                // back in guard by this time
    public float activeStart, activeEnd;  // hit window
    public float cancelFrom;              // next move in the string may start from here
    public Limb limb;
    public float damage, hitstun, push, launch, hitstop, lunge, shake;
    public bool knockdown, high, heavy;
    public float reach;                   // rough distance at which the AI throws it

    // Mocap binding (times below are in clip seconds once bound)
    public string clip;
    public float speed = 1.15f;           // playback rate
    public float windup = 0.2f;           // max real seconds from start to impact (long wind-ups get trimmed)
    public float recovery = 0.3f;         // real seconds after impact before the move ends
    public float clipStart;

    public void Bind(AnimLibrary lib)
    {
        var e = lib != null && clip != null ? lib[clip] : null;
        if (e == null) return;
        clipStart = Mathf.Max(0, e.impact - windup * speed);
        activeStart = e.impact - 0.06f * speed;
        activeEnd = e.impact + 0.05f * speed;
        cancelFrom = e.impact + 0.07f * speed;
        duration = Mathf.Min(e.length, e.impact + recovery * speed);
        limb = e.limb;
        reach = Mathf.Max(0.9f, e.reach + e.travel * 0.6f + 0.25f);
    }

    // Pose at time t (seconds, at attack speed 1): guard -> keys -> guard.
    public Pose Sample(float t, Pose guard)
    {
        if (t <= keys[0].t) return Pose.Lerp(guard, keys[0].pose, Ease.OutCubic(t / keys[0].t));
        for (int i = 1; i < keys.Length; i++)
        {
            if (t <= keys[i].t)
            {
                float k = (t - keys[i - 1].t) / (keys[i].t - keys[i - 1].t);
                return Pose.Lerp(keys[i - 1].pose, keys[i].pose, Ease.OutCubic(k));
            }
        }
        var last = keys[keys.Length - 1];
        var g = Unwrapped(guard, last.pose);
        return Pose.Lerp(last.pose, g, Ease.InOut((t - last.t) / Mathf.Max(0.01f, duration - last.t)));
    }

    // Guard with its hip yaw moved by whole turns to sit nearest the given pose, so a spin
    // finishes going forward instead of unwinding.
    static Pose Unwrapped(Pose guard, Pose near)
    {
        var g = guard.Clone();
        float y = g.rot[0].y, target = near.rot[0].y;
        while (y - target > 180) y -= 360;
        while (target - y > 180) y += 360;
        g.rot[0].y = y;
        return g;
    }
}

public static class Moves
{
    public static readonly Pose Guard = new Pose().Hip(-0.07f)
        .Hips(4, 22).Spine(3, -6).Chest(3, -6).Head(4, -10)
        .ArmL(60, 18, 12, 105).ArmR(55, 28, 12, 128)
        .LegL(18, 6, 24).LegR(-18, 8, 18, 10);

    // Additive walk layer (scaled by a sine of the step phase in Fighter).
    public static readonly Pose Stride = new Pose()
        .Set(J.ThighL, -24).Set(J.ThighR, 24).Set(J.FootL, 24).Set(J.FootR, -24)
        .Set(J.UpperArmL, 8).Set(J.UpperArmR, -8).Set(J.Hips, 0, 4);

    public static readonly Pose Dash = Guard.Clone().Hip(-0.14f, 0.05f)
        .Hips(16, 22).Spine(6, -6).Chest(6, -6).Head(-6, -10)
        .LegL(40, 6, 55).LegR(-30, 8, 40, 20);

    public static readonly Pose BackDash = Guard.Clone().Hip(-0.1f, -0.05f)
        .Hips(-10, 22).Chest(-4, -6).Head(8, -10)
        .LegL(35, 6, 20).LegR(-10, 8, 45, 10);

    public static readonly Pose HitHigh = new Pose().Hip(-0.05f, -0.1f)
        .Hips(-6, 22).Spine(-10, -6).Chest(-14, -6, 6).Head(-32, 18, 12)
        .ArmL(40, 0, 32, 70).ArmR(35, 10, 25, 90)
        .LegL(26, 6, 30).LegR(-14, 8, 26, 10);

    public static readonly Pose HitBody = new Pose().Hip(-0.14f, -0.06f)
        .Hips(16, 22).Spine(18, -6).Chest(16, -6).Head(12, -10)
        .ArmL(25, 35, 5, 120).ArmR(20, 35, 5, 125)
        .LegL(28, 6, 44).LegR(-8, 8, 34, 10);

    public static readonly Pose Air = new Pose()
        .Hips(-45, 0).Spine(-10, 0).Chest(-10, 0).Head(-25, 0)
        .ArmL(110, -10, 70, 30).ArmR(100, -10, 75, 30)
        .LegL(45, 10, 70).LegR(20, 12, 35);

    public static readonly Pose Down = new Pose().Hip(-0.84f)
        .Hips(-90, 0).Spine(-2, 0).Chest(-2, 0).Head(12, 25)
        .ArmL(10, 0, 55, 25).ArmR(0, 0, 45, 15)
        .LegL(14, 12, 28).LegR(2, 14, 6);

    public static readonly Pose Kneel = new Pose().Hip(-0.47f)
        .Hips(10, 10).Spine(6, 0).Chest(6, -5).Head(0, -5)
        .ArmL(40, 10, 20, 80).ArmR(20, 0, 30, 30)
        .LegL(90, 8, 90).LegR(0, 10, 90, 60);

    public static readonly Pose Victory = new Pose()
        .Hips(0, 10).Spine(-5, 0).Chest(-8, -5).Head(-12, -5)
        .ArmR(175, 0, 12, 20).ArmL(60, 40, 10, 130)
        .LegL(6, 8, 6).LegR(-6, 8, 6);

    public static readonly Pose Salute = new Pose().Hip(-0.02f)
        .Hips(12, 0).Spine(6, 0).Chest(8, 0).Head(12, 0)
        .ArmR(78, 48, 0, 95).ArmL(80, 52, 0, 92)
        .LegL(2, 6, 4).LegR(-2, 6, 4);

    public static readonly Pose Roar = new Pose().Hip(-0.12f)
        .Hips(-4, 0).Spine(-8, 0).Chest(-10, 0).Head(-18, 0)
        .ArmR(20, 0, 70, 75).ArmL(20, 0, 70, 75)
        .LegL(20, 16, 30).LegR(20, 16, 30);

    // ------------------------------------------------------------------ defence / throws
    public static readonly Pose Block = new Pose().Hip(-0.1f)
        .Hips(10, 18).Spine(6, -4).Chest(8, -4).Head(12, -8)
        .ArmL(105, 38, 12, 145).ArmR(100, 44, 12, 150)
        .LegL(20, 8, 32).LegR(-16, 10, 26, 10);

    public static readonly Pose BlockHit = new Pose().Hip(-0.14f, -0.07f)
        .Hips(2, 18).Spine(2, -4).Chest(0, -4).Head(6, -8)
        .ArmL(108, 40, 14, 150).ArmR(104, 46, 14, 152)
        .LegL(26, 8, 40).LegR(-12, 10, 34, 10);

    public static readonly Pose Parried = new Pose().Hip(-0.06f, -0.08f)
        .Hips(-12, 22).Spine(-10, -6).Chest(-12, -6).Head(-20, -10)
        .ArmL(95, -30, 75, 25).ArmR(90, -30, 80, 20)
        .LegL(24, 8, 26).LegR(-20, 10, 20, 10);

    public static readonly Pose GuardBreak = new Pose().Hip(-0.18f, -0.05f)
        .Hips(22, 15).Spine(15, 0).Chest(12, 0).Head(18, 0)
        .ArmL(10, 10, 25, 30).ArmR(5, 10, 25, 25)
        .LegL(30, 10, 50).LegR(-5, 12, 40, 10);

    public static readonly Pose SideStepR = Guard.Clone().Hip(-0.12f).Hips(8, 22, -14).Head(4, -10, 10)
        .LegL(10, 25, 30).LegR(-10, 5, 20, 10);

    public static readonly Pose SideStepL = Guard.Clone().Hip(-0.12f).Hips(8, 22, 14).Head(4, -10, -10)
        .LegL(10, 5, 30).LegR(-10, 25, 20, 10);

    public static readonly Pose Grab = new Pose().Hip(-0.12f, 0.12f)
        .Hips(18, 5).Spine(6, 0).Chest(6, 0).Head(0, -5)
        .ArmL(88, 25, 5, 25).ArmR(88, 25, 5, 25)
        .LegL(35, 6, 40).LegR(-20, 8, 20, 10);

    public static readonly Pose Hoist = new Pose().Hip(-0.08f)
        .Hips(-8, 0).Spine(-6, 0).Chest(-8, 0).Head(-10, 0)
        .ArmL(115, 35, 10, 70).ArmR(115, 35, 10, 70)
        .LegL(12, 14, 22).LegR(12, 14, 22);

    public static readonly Pose Hurl = new Pose().Hip(-0.1f, 0.1f)
        .Hips(20, 30).Spine(10, 10).Chest(10, 10).Head(5, -20)
        .ArmL(80, -10, 40, 10).ArmR(70, -30, 60, 10)
        .LegL(30, 8, 35).LegR(-25, 10, 20, 15);

    public static readonly Pose Held = new Pose()
        .Hips(-15, 0).Spine(-5, 0).Chest(-10, 0).Head(-25, 0)
        .ArmL(40, 20, 30, 60).ArmR(40, 20, 30, 60)
        .LegL(15, 8, 40, 30).LegR(5, 8, 50, 30);

    // ------------------------------------------------------------------ attacks
    public static readonly Move Jab = new Move
    {
        clip = "Jab", speed = 1.7f, windup = 0.09f, recovery = 0.16f,
        name = "Jab", limb = Limb.HandL, high = true,
        keys = new[]
        {
            (0.10f, new Pose().Hip(-0.08f, 0.08f).Hips(8, 30).Spine(2, 5).Chest(2, 10).Head(4, -40)
                .ArmL(88, -38, 0, 4).ArmR(52, 30, 12, 125).LegL(24, 6, 22).LegR(-22, 8, 16, 12)),
            (0.16f, new Pose().Hip(-0.08f, 0.08f).Hips(8, 30).Spine(2, 5).Chest(2, 10).Head(4, -40)
                .ArmL(86, -38, 0, 8).ArmR(52, 30, 12, 125).LegL(24, 6, 22).LegR(-22, 8, 16, 12)),
        },
        duration = 0.30f, activeStart = 0.07f, activeEnd = 0.15f, cancelFrom = 0.13f,
        damage = 5, hitstun = 0.32f, push = 1.2f, hitstop = 0.05f, lunge = 0.12f, shake = 0.05f, reach = 1.2f,
    };

    public static readonly Move Cross = new Move
    {
        clip = "Cross", speed = 1.65f, windup = 0.11f, recovery = 0.18f,
        name = "Cross", limb = Limb.HandR, high = true,
        keys = new[]
        {
            (0.06f, Guard.Clone().Hips(5, 32).Chest(3, 0).ArmR(40, 25, 15, 130)),
            (0.12f, new Pose().Hip(-0.1f, 0.12f).Hips(10, -15).Spine(3, -10).Chest(3, -12).Head(2, 35)
                .ArmR(90, -34, 0, 3).ArmL(55, 35, 10, 125).LegL(28, 6, 30).LegR(-26, 10, 14, 25)),
            (0.18f, new Pose().Hip(-0.1f, 0.12f).Hips(10, -15).Spine(3, -10).Chest(3, -12).Head(2, 35)
                .ArmR(88, -34, 0, 8).ArmL(55, 35, 10, 125).LegL(28, 6, 30).LegR(-26, 10, 14, 25)),
        },
        duration = 0.36f, activeStart = 0.09f, activeEnd = 0.17f, cancelFrom = 0.16f,
        damage = 7, hitstun = 0.36f, push = 1.6f, hitstop = 0.06f, lunge = 0.18f, shake = 0.07f, reach = 1.25f,
    };

    public static readonly Move Hook = new Move
    {
        clip = "Hook", speed = 1.6f, windup = 0.13f, recovery = 0.2f,
        name = "Hook", limb = Limb.HandL, high = true,
        keys = new[]
        {
            (0.09f, Guard.Clone().Hip(-0.1f).Hips(6, 5).Chest(2, -15).Head(4, 10).ArmL(70, -30, 20, 100)),
            (0.15f, new Pose().Hip(-0.1f, 0.12f).Hips(8, 40).Spine(2, 10).Chest(2, 12).Head(3, -55)
                .ArmL(8, 0, 88, 75).ArmR(55, 30, 12, 125).LegL(26, 6, 30, 0, -20).LegR(-20, 8, 18, 15)),
            (0.22f, new Pose().Hip(-0.1f, 0.12f).Hips(8, 48).Spine(2, 12).Chest(2, 14).Head(3, -65)
                .ArmL(8, 10, 85, 80).ArmR(55, 30, 12, 125).LegL(26, 6, 30, 0, -20).LegR(-20, 8, 18, 15)),
        },
        duration = 0.46f, activeStart = 0.12f, activeEnd = 0.21f, cancelFrom = 0.22f,
        damage = 9, hitstun = 0.4f, push = 1.8f, hitstop = 0.07f, lunge = 0.14f, shake = 0.1f, reach = 1.1f,
    };

    public static readonly Move Uppercut = new Move
    {
        clip = "Uppercut", speed = 1.55f, windup = 0.15f, recovery = 0.24f,
        name = "Rising Dragon", limb = Limb.HandR, high = true, knockdown = true, heavy = true,
        keys = new[]
        {
            (0.12f, new Pose().Hip(-0.22f, 0.05f).Hips(20, -5).Spine(8, -10).Chest(5, -10).Head(-10, 20)
                .ArmR(10, 10, 15, 95).ArmL(55, 30, 12, 120).LegL(40, 8, 62).LegR(-10, 10, 48, 10)),
            (0.19f, new Pose().Hip(0.06f, 0.14f).Hips(-8, -35).Spine(-6, -10).Chest(-6, -8).Head(-10, 45)
                .ArmR(150, -20, 5, 40).ArmL(30, 10, 30, 90).LegL(10, 5, 5, 20).LegR(-15, 8, 5, 35)),
            (0.30f, new Pose().Hip(0.08f, 0.14f).Hips(-10, -38).Spine(-6, -10).Chest(-8, -8).Head(-12, 45)
                .ArmR(160, -20, 5, 35).ArmL(30, 10, 30, 90).LegL(10, 5, 5, 25).LegR(-15, 8, 5, 40)),
        },
        duration = 0.62f, activeStart = 0.14f, activeEnd = 0.27f, cancelFrom = 0.42f,
        damage = 12, hitstun = 0.5f, push = 1.0f, launch = 5.4f, hitstop = 0.11f, lunge = 0.16f, shake = 0.22f, reach = 1.05f,
    };

    public static readonly Move FrontKick = new Move
    {
        clip = "FrontKick", speed = 1.6f, windup = 0.15f, recovery = 0.22f,
        name = "Front Kick", limb = Limb.FootR,
        keys = new[]
        {
            (0.10f, Guard.Clone().Hip(-0.03f, -0.03f).Hips(-8, 5).Chest(4, -5).Head(4, -5)
                .LegR(90, 5, 110).LegL(8, 5, 20)),
            (0.17f, Guard.Clone().Hip(0f, 0.1f).Hips(-16, 8).Spine(4, 0).Chest(8, -5).Head(8, -5)
                .LegR(76, 5, 0, -40).LegL(5, 4, 12)),
            (0.25f, Guard.Clone().Hip(0f, 0.1f).Hips(-16, 8).Spine(4, 0).Chest(8, -5).Head(8, -5)
                .LegR(74, 5, 4, -40).LegL(5, 4, 12)),
            (0.34f, Guard.Clone().Hip(-0.03f).Hips(-6, 10).LegR(80, 5, 100).LegL(8, 5, 20)),
        },
        duration = 0.5f, activeStart = 0.13f, activeEnd = 0.25f, cancelFrom = 0.3f,
        damage = 9, hitstun = 0.45f, push = 4.5f, hitstop = 0.08f, lunge = 0.2f, shake = 0.14f, reach = 1.4f,
    };

    public static readonly Move Roundhouse = new Move
    {
        clip = "Roundhouse", speed = 1.6f, windup = 0.17f, recovery = 0.24f,
        name = "Roundhouse", limb = Limb.FootR, high = true, heavy = true,
        keys = new[]
        {
            (0.12f, new Pose().Hip(-0.05f).Hips(0, -40, 25).Spine(0, 10, -10).Chest(0, 10, -8).Head(0, 20, -8)
                .LegR(20, 70, 110, 30).LegL(0, 25, 15).ArmL(55, 20, 25, 100).ArmR(30, 0, 40, 60)),
            (0.21f, new Pose().Hip(0f, 0.08f).Hips(0, -85, 35).Spine(0, 15, -15).Chest(0, 15, -10).Head(0, 50, -10)
                .LegR(15, 60, 5, 40).LegL(0, 35, 10).ArmL(55, 20, 25, 100).ArmR(10, -20, 60, 30)),
            (0.30f, new Pose().Hip(0f, 0.08f).Hips(0, -100, 33).Spine(0, 15, -15).Chest(0, 15, -10).Head(0, 60, -10)
                .LegR(15, 58, 15, 40).LegL(0, 33, 10).ArmL(55, 20, 25, 100).ArmR(10, -20, 60, 30)),
            (0.42f, new Pose().Hip(-0.05f).Hips(0, -60, 20).Spine(0, 10, -8).Chest(0, 10, -6).Head(0, 40, -6)
                .LegR(20, 50, 100, 30).LegL(0, 20, 15).ArmL(55, 20, 25, 100).ArmR(30, 0, 40, 60)),
        },
        duration = 0.62f, activeStart = 0.16f, activeEnd = 0.3f, cancelFrom = 0.36f,
        damage = 13, hitstun = 0.5f, push = 3f, hitstop = 0.1f, lunge = 0.15f, shake = 0.2f, reach = 1.35f,
    };

    public static readonly Move AxeKick = new Move
    {
        clip = "HighKick", speed = 1.55f, windup = 0.19f, recovery = 0.26f,
        name = "High Kick", limb = Limb.FootR, high = true, knockdown = true, heavy = true,
        keys = new[]
        {
            (0.20f, new Pose().Hip(0f, -0.02f).Hips(-15, 10).Spine(0, 0).Chest(5, -5).Head(6, -10)
                .LegR(160, 15, 0, 30).LegL(0, 5, 10).ArmL(60, 20, 40, 60).ArmR(40, 0, 50, 40)),
            (0.31f, new Pose().Hip(-0.15f, 0.14f).Hips(25, 15).Spine(10, 0).Chest(10, -5).Head(-10, -10)
                .LegR(45, 5, 5, 20).LegL(30, 5, 50).ArmL(60, 20, 30, 90).ArmR(30, 10, 40, 60)),
            (0.42f, new Pose().Hip(-0.15f, 0.14f).Hips(22, 15).Spine(8, 0).Chest(8, -5).Head(-8, -10)
                .LegR(35, 5, 20, 10).LegL(30, 5, 50).ArmL(60, 20, 30, 90).ArmR(30, 10, 40, 60)),
        },
        duration = 0.75f, activeStart = 0.22f, activeEnd = 0.33f, cancelFrom = 0.62f,
        damage = 16, hitstun = 0.6f, push = 2f, launch = 3.5f, hitstop = 0.13f, lunge = 0.22f, shake = 0.3f, reach = 1.3f,
    };

    public static readonly Move SpinKick = new Move
    {
        clip = "SpinBackKick", speed = 1.55f, windup = 0.2f, recovery = 0.28f,
        name = "Spinning Back Kick", limb = Limb.FootR, high = true, knockdown = true, heavy = true,
        keys = new[]
        {
            (0.10f, new Pose().Hip(-0.08f).Hips(0, 90).Spine(0, 10).Chest(0, 10).Head(0, -70)
                .LegR(10, 20, 40, 20).LegL(10, 5, 25).ArmL(70, 40, 20, 120).ArmR(70, 40, 20, 120)),
            (0.20f, new Pose().Hip(-0.03f).Hips(0, 200, 25).Spine(0, 0, -10).Chest(0, 0, -8).Head(0, -70)
                .LegR(0, 70, 30, 40).LegL(0, 25, 15).ArmL(40, 0, 60, 60).ArmR(40, 0, 60, 60)),
            (0.30f, new Pose().Hip(0f, 0.1f).Hips(0, 290, 40).Spine(0, 0, -15).Chest(0, 0, -10).Head(0, 70)
                .LegR(5, 85, 0, 40).LegL(0, 40, 10).ArmL(20, -30, 70, 20).ArmR(20, -30, 70, 20)),
            (0.42f, new Pose().Hip(-0.05f, 0.1f).Hips(0, 345, 20).Spine(0, 0, -8).Chest(0, 0, -6).Head(0, 20)
                .LegR(0, 40, 35, 20).LegL(0, 20, 15).ArmL(40, 0, 50, 60).ArmR(40, 0, 50, 60)),
        },
        duration = 0.8f, activeStart = 0.2f, activeEnd = 0.36f, cancelFrom = 0.66f,
        damage = 18, hitstun = 0.6f, push = 6f, launch = 4.5f, hitstop = 0.15f, lunge = 0.4f, shake = 0.35f, reach = 1.45f,
    };

    // forward + heavy: knee launcher that pops the opponent up for a juggle
    public static readonly Move RisingKnee = new Move
    {
        clip = "Knee", speed = 1.6f, windup = 0.14f, recovery = 0.22f,
        name = "Rising Knee", limb = Limb.KneeR, knockdown = true, heavy = true,
        keys = new[]
        {
            (0.08f, Guard.Clone().Hip(-0.12f, 0.05f).Hips(10, 15).Chest(8, -5).ArmL(80, 35, 5, 90).ArmR(80, 35, 5, 90).LegR(40, 5, 80).LegL(20, 5, 30)),
            (0.16f, new Pose().Hip(0.04f, 0.14f).Hips(-6, 12).Spine(4, 0).Chest(8, -4).Head(10, -8)
                .ArmL(70, 40, 5, 110).ArmR(70, 40, 5, 110).LegR(118, 5, 125, 20).LegL(-5, 5, 5, 40)),
            (0.26f, new Pose().Hip(0.05f, 0.14f).Hips(-8, 12).Spine(4, 0).Chest(8, -4).Head(10, -8)
                .ArmL(70, 40, 5, 110).ArmR(70, 40, 5, 110).LegR(122, 5, 128, 20).LegL(-5, 5, 5, 45)),
        },
        duration = 0.56f, activeStart = 0.1f, activeEnd = 0.24f, cancelFrom = 0.32f,
        damage = 10, hitstun = 0.5f, push = 0.5f, launch = 5.6f, hitstop = 0.1f, lunge = 0.25f, shake = 0.2f, reach = 0.95f,
    };

    // Brakka's close-range branch uses the unused mocap clips already in the project.  Giving the
    // two fighters different routes makes the same buttons produce recognisably different styles:
    // Kaito stays a flowing striker, while Brakka crowds the opponent with hooks, elbows and a
    // brutal headbutt ender.
    public static readonly Move RightHook = new Move
    {
        clip = "RightHook", speed = 1.5f, windup = 0.14f, recovery = 0.22f,
        name = "Iron Hook", limb = Limb.HandR, high = true, heavy = true,
        keys = new[] { (0.12f, Guard), (0.26f, Guard) },
        duration = 0.52f, activeStart = 0.14f, activeEnd = 0.26f, cancelFrom = 0.30f,
        damage = 12, hitstun = 0.5f, push = 2.6f, hitstop = 0.1f, lunge = 0.16f, shake = 0.2f, reach = 1.05f,
    };

    public static readonly Move Elbow = new Move
    {
        clip = "Elbow", speed = 1.6f, windup = 0.1f, recovery = 0.18f,
        name = "Elbow Crush", limb = Limb.ElbowR, high = true, heavy = true,
        keys = new[] { (0.08f, Guard), (0.20f, Guard) },
        duration = 0.42f, activeStart = 0.1f, activeEnd = 0.2f, cancelFrom = 0.24f,
        damage = 11, hitstun = 0.42f, push = 1.7f, hitstop = 0.09f, lunge = 0.2f, shake = 0.16f, reach = 0.82f,
    };

    public static readonly Move Headbutt = new Move
    {
        clip = "Headbutt", speed = 1.42f, windup = 0.15f, recovery = 0.27f,
        name = "Skullbreaker", limb = Limb.Head, high = true, knockdown = true, heavy = true,
        keys = new[] { (0.12f, Guard), (0.30f, Guard) },
        duration = 0.62f, activeStart = 0.16f, activeEnd = 0.30f, cancelFrom = 0.46f,
        damage = 17, hitstun = 0.62f, push = 4.2f, launch = 3.2f, hitstop = 0.14f, lunge = 0.18f, shake = 0.32f, reach = 0.8f,
    };

    public static readonly Move[] All = { Jab, Cross, Hook, Uppercut, FrontKick, Roundhouse, AxeKick, SpinKick, RisingKnee, RightHook, Elbow, Headbutt };

    // Strings: which move follows which button during the cancel window.
    static readonly Dictionary<(Move, Btn), Move> next = new Dictionary<(Move, Btn), Move>
    {
        { (Jab, Btn.Light), Cross }, { (Cross, Btn.Light), Hook }, { (Hook, Btn.Light), Uppercut },
        { (Jab, Btn.Heavy), FrontKick }, { (Cross, Btn.Heavy), SpinKick }, { (Hook, Btn.Heavy), AxeKick },
        { (Roundhouse, Btn.Heavy), SpinKick }, { (Roundhouse, Btn.Light), Hook }, { (FrontKick, Btn.Heavy), AxeKick },
        { (FrontKick, Btn.Light), Cross },
        { (RisingKnee, Btn.Light), Jab }, { (RisingKnee, Btn.Heavy), Roundhouse }, { (Uppercut, Btn.Light), Jab },
    };

    static readonly Dictionary<(Move, Btn), Move> bruteNext = new Dictionary<(Move, Btn), Move>
    {
        { (Jab, Btn.Heavy), Elbow },
        { (Cross, Btn.Light), RightHook },
        { (RightHook, Btn.Heavy), Headbutt },
    };

    public static void Bind(AnimLibrary lib)
    {
        foreach (var m in All) m.Bind(lib);
    }

    public static Move Start(Btn b) => b == Btn.Light ? Jab : Roundhouse;

    public static Move Next(Move current, Btn b, bool brute = false)
    {
        if (brute && bruteNext.TryGetValue((current, b), out var heavy)) return heavy;
        return next.TryGetValue((current, b), out var m) ? m : null;
    }
}
