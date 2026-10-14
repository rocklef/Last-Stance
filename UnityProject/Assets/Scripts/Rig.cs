using UnityEngine;

// The fighter's joints, in the order poses store them.
public enum J { Hips, Spine, Chest, Head, UpperArmR, ForeArmR, HandR, UpperArmL, ForeArmL, HandL, ThighR, ShinR, FootR, ThighL, ShinL, FootL, Count }

// A full-body pose: local Euler angles per joint plus an offset for the hips.
// Joints rest with identity rotation (standing straight, arms down, facing +Z, right = +X),
// so angles read as: X pitch (+ leans a torso forward, - swings a limb forward), Y yaw (+ turns right), Z roll.
// Poses are lerped as Euler angles, which lets a move spin the hips a full 360.
public class Pose
{
    public readonly Vector3[] rot = new Vector3[(int)J.Count];
    public Vector3 hip;

    public Pose Clone()
    {
        var p = new Pose { hip = hip };
        System.Array.Copy(rot, p.rot, rot.Length);
        return p;
    }

    public Pose Set(J j, float x, float y = 0, float z = 0) { rot[(int)j] = new Vector3(x, y, z); return this; }

    // Torso: X leans forward, Y turns right.
    public Pose Hips(float x, float y, float z = 0) => Set(J.Hips, x, y, z);
    public Pose Spine(float x, float y, float z = 0) => Set(J.Spine, x, y, z);
    public Pose Chest(float x, float y, float z = 0) => Set(J.Chest, x, y, z);
    public Pose Head(float x, float y, float z = 0) => Set(J.Head, x, y, z);

    // Arms, mirrored: raise swings the arm forward-up (90 = pointing straight ahead), inward yaws it
    // across the body, outward lifts it sideways, elbow flexes the forearm forward.
    public Pose ArmR(float raise, float inward, float outward, float elbow, float wrist = 0)
    {
        Set(J.UpperArmR, -raise, -inward, outward);
        Set(J.ForeArmR, -elbow);
        return Set(J.HandR, wrist);
    }

    public Pose ArmL(float raise, float inward, float outward, float elbow, float wrist = 0)
    {
        Set(J.UpperArmL, -raise, inward, -outward);
        Set(J.ForeArmL, -elbow);
        return Set(J.HandL, wrist);
    }

    // Legs, mirrored: lift swings the thigh forward, outward spreads it sideways, knee bends the shin back.
    // The foot is kept level with the hips automatically; point tips the toes down (negative lifts them).
    public Pose LegR(float lift, float outward, float knee, float point = 0, float twist = 0)
    {
        Set(J.ThighR, -lift, twist, outward);
        Set(J.ShinR, knee);
        return Set(J.FootR, lift - knee + point);
    }

    public Pose LegL(float lift, float outward, float knee, float point = 0, float twist = 0)
    {
        Set(J.ThighL, -lift, -twist, -outward);
        Set(J.ShinL, knee);
        return Set(J.FootL, lift - knee + point);
    }

    // Hips offset: y lowers/raises, z shifts forward, x shifts right.
    public Pose Hip(float y, float z = 0, float x = 0) { hip = new Vector3(x, y, z); return this; }

    public static Pose Lerp(Pose a, Pose b, float t)
    {
        var p = new Pose { hip = Vector3.LerpUnclamped(a.hip, b.hip, t) };
        for (int i = 0; i < p.rot.Length; i++) p.rot[i] = Vector3.LerpUnclamped(a.rot[i], b.rot[i], t);
        return p;
    }

    // Adds b's angles/offset on top of a (for layering breathing, hit flinches, walk swing).
    public static Pose Add(Pose a, Pose b, float w)
    {
        var p = new Pose { hip = a.hip + b.hip * w };
        for (int i = 0; i < p.rot.Length; i++) p.rot[i] = a.rot[i] + b.rot[i] * w;
        return p;
    }
}

// Drives the jointed fighter model: applies a Pose each frame, cross-fading from whatever
// the body was doing when Fade() was called.
public class Rig : MonoBehaviour
{
    public Transform[] joints = new Transform[(int)J.Count];
    Vector3 hipRest;
    Quaternion[] snapshot = new Quaternion[(int)J.Count];
    Vector3 snapshotHip;
    float fadeT = 1, fadeDur = 0;

    static readonly string[] Names =
    {
        "Hips", "Spine", "Chest", "Head", "UpperArm_R", "ForeArm_R", "Hand_R", "UpperArm_L", "ForeArm_L", "Hand_L",
        "Thigh_R", "Shin_R", "Foot_R", "Thigh_L", "Shin_L", "Foot_L",
    };

    void Awake() => Bind();

    public void Bind()
    {
        for (int i = 0; i < Names.Length; i++)
            if (joints[i] == null) joints[i] = Find(transform, Names[i]);
        hipRest = joints[0].localPosition;
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = Find(c, name);
            if (r != null) return r;
        }
        return null;
    }

    public Transform this[J j] => joints[(int)j];

    // Starts a cross-fade from the current body pose into whatever Apply() is given next.
    public void Fade(float duration)
    {
        for (int i = 0; i < joints.Length; i++) snapshot[i] = joints[i].localRotation;
        snapshotHip = joints[0].localPosition - hipRest;
        fadeT = 0;
        fadeDur = Mathf.Max(0.0001f, duration);
    }

    public void Apply(Pose p, float dt)
    {
        fadeT += dt;
        float k = fadeT >= fadeDur ? 1 : Ease.OutCubic(fadeT / fadeDur);
        for (int i = 0; i < joints.Length; i++)
        {
            var q = Quaternion.Euler(p.rot[i]);
            joints[i].localRotation = k >= 1 ? q : Quaternion.Slerp(snapshot[i], q, k);
        }
        joints[0].localPosition = hipRest + (k >= 1 ? p.hip : Vector3.Lerp(snapshotHip, p.hip, k));
    }
}

public static class Ease
{
    public static float OutCubic(float t) { t = 1 - Mathf.Clamp01(t); return 1 - t * t * t; }
    public static float InOut(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    public static float OutBack(float t) { t = Mathf.Clamp01(t) - 1; return 1 + t * t * (2.2f * t + 1.2f); }
    public static float In(float t) { t = Mathf.Clamp01(t); return t * t; }
}
