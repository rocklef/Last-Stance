using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

// Builds Assets/Generated/AnimLibrary.asset from the Mixamo clips: plays each clip on the Kaito model
// frame by frame and records when and with which limb each strike lands (the limb that travels
// furthest forward), how far it reaches and how high. Also renders a contact sheet of the clips.
public static class AnimAnalyzer
{
    public const string LibraryPath = "Assets/Generated/AnimLibrary.asset";
    public const string ModelPath = MixamoImport.CharDir + "Leonard.fbx";

    // Clips that deliver a strike; everything else is analysed only for length / key moments.
    public static readonly string[] Strikes =
    {
        "Jab", "Cross", "Hook", "Uppercut", "FrontKick", "Roundhouse", "HighKick", "SpinBackKick", "Knee",
        "Headbutt", "Elbow", "RightHook", "HurricaneKick", "FlyingKneeCombo", "SideKick", "Martelo",
    };

    static readonly (Limb limb, HumanBodyBones bone)[] Limbs =
    {
        (Limb.HandL, HumanBodyBones.LeftHand), (Limb.HandR, HumanBodyBones.RightHand),
        (Limb.FootL, HumanBodyBones.LeftFoot), (Limb.FootR, HumanBodyBones.RightFoot),
        (Limb.KneeL, HumanBodyBones.LeftLowerLeg), (Limb.KneeR, HumanBodyBones.RightLowerLeg),
        (Limb.ElbowL, HumanBodyBones.LeftLowerArm), (Limb.ElbowR, HumanBodyBones.RightLowerArm),
        (Limb.Head, HumanBodyBones.Head),
    };

    public static AnimationClip[] LoadClips() =>
        Directory.GetFiles(MixamoImport.AnimDir, "*.fbx")
            .SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')).OfType<AnimationClip>())
            .Where(c => !c.name.StartsWith("__preview__"))
            .OrderBy(c => c.name).ToArray();

    [MenuItem("Last Stance/Analyse Animations")]
    public static AnimLibrary Build()
    {
        Directory.CreateDirectory("Assets/Generated");
        var clips = LoadClips();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        var anim = go.GetComponent<Animator>();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim.Rebind();

        var lib = AssetDatabase.LoadAssetAtPath<AnimLibrary>(LibraryPath);
        bool isNew = lib == null;
        if (isNew) lib = ScriptableObject.CreateInstance<AnimLibrary>();
        lib.entries.Clear();

        var graph = PlayableGraph.Create("analyse");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "o", anim);
        foreach (var clip in clips)
        {
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(false);
            output.SetSourcePlayable(p);
            var e = new AnimLibrary.Entry { name = clip.name, clip = clip, length = clip.length };
            bool strike = Strikes.Contains(clip.name);
            float best = float.MinValue;
            var start = new Dictionary<Limb, float>();
            float lowestHips = float.MaxValue, highestHands = float.MinValue;
            for (float t = 0; t <= clip.length + 1e-4f; t += 1f / 60f)
            {
                p.SetTime(t);
                graph.Evaluate(0);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips).position;
                if (hips.y < lowestHips) { lowestHips = hips.y; if (!strike) e.impact = t; }
                if (clip.name == "Block")
                {
                    float hy = anim.GetBoneTransform(HumanBodyBones.LeftHand).position.y + anim.GetBoneTransform(HumanBodyBones.RightHand).position.y;
                    if (hy > highestHands) { highestHands = hy; e.impact = t; }
                }
                if (!strike) continue;
                foreach (var (limb, bone) in Limbs)
                {
                    // some strikes are delivered by a specific body part
                    if (clip.name == "Knee" && limb != Limb.KneeL && limb != Limb.KneeR) continue;
                    if (clip.name == "Headbutt" && limb != Limb.Head) continue;
                    if (clip.name == "Elbow" && limb != Limb.ElbowL && limb != Limb.ElbowR) continue;
                    var b = anim.GetBoneTransform(bone).position;
                    if (!start.ContainsKey(limb)) start[limb] = b.z;
                    // favour the limb that travels furthest forward from where it started
                    float score = (b.z - start[limb]) + b.z * 0.25f;
                    if (clip.name == "Uppercut") score = b.y + b.z * 0.3f;   // uppercuts drive up, not out
                    if ((limb == Limb.KneeL || limb == Limb.KneeR || limb == Limb.ElbowL || limb == Limb.ElbowR || limb == Limb.Head)
                        && clip.name != "Knee" && clip.name != "Headbutt" && clip.name != "Elbow") score -= 0.12f;
                    if (score > best)
                    {
                        best = score;
                        e.impact = t;
                        e.limb = limb;
                        e.reach = b.z;
                        e.height = b.y;
                    }
                }
            }
            e.travel = strike ? clip.averageSpeed.z * e.impact : 0;
            if (clip.name == "GrabSlam") AnalyseGrab(e, anim, graph, p);
            if (clip.name == "GettingUp") AnalyseGetUp(e, anim, graph, p);
            lib.entries.Add(e);
            Debug.Log($"AnimAnalyzer: {clip.name,-18} len={clip.length:0.00} impact={e.impact:0.00} limb={(strike ? e.limb.ToString() : "-")} reach={e.reach:0.00} height={e.height:0.00} travel={e.travel:0.00}");
        }
        graph.Destroy();
        Object.DestroyImmediate(go);
        if (isNew) AssetDatabase.CreateAsset(lib, LibraryPath);
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return lib;
    }

    // Grab-and-slam: contact = hands furthest forward in the first half, slam = hands lowest after that.
    static void AnalyseGrab(AnimLibrary.Entry e, Animator anim, PlayableGraph graph, AnimationClipPlayable p)
    {
        float bestZ = float.MinValue, lowY = float.MaxValue;
        for (float t = 0; t <= e.length * 0.6f; t += 1f / 60f)
        {
            p.SetTime(t); graph.Evaluate(0);
            float z = (anim.GetBoneTransform(HumanBodyBones.LeftHand).position.z + anim.GetBoneTransform(HumanBodyBones.RightHand).position.z) * 0.5f;
            if (z > bestZ) { bestZ = z; e.contact = t; }
        }
        for (float t = e.contact; t <= e.length; t += 1f / 60f)
        {
            p.SetTime(t); graph.Evaluate(0);
            float y = (anim.GetBoneTransform(HumanBodyBones.LeftHand).position.y + anim.GetBoneTransform(HumanBodyBones.RightHand).position.y) * 0.5f;
            if (y < lowY) { lowY = y; e.impact = t; }
        }
        Debug.Log($"AnimAnalyzer: GrabSlam contact={e.contact:0.00} slam={e.impact:0.00}");
    }

    // Get-up: from the lowest (lying) frame, the first frame the hips are back near standing height.
    static void AnalyseGetUp(AnimLibrary.Entry e, Animator anim, PlayableGraph graph, AnimationClipPlayable p)
    {
        float standY = 0;
        for (float t = e.length - 0.5f; t <= e.length; t += 1f / 60f) { p.SetTime(t); graph.Evaluate(0); standY = Mathf.Max(standY, anim.GetBoneTransform(HumanBodyBones.Hips).position.y); }
        e.recover = e.length;
        for (float t = e.impact; t <= e.length; t += 1f / 60f)
        {
            p.SetTime(t); graph.Evaluate(0);
            if (anim.GetBoneTransform(HumanBodyBones.Hips).position.y >= standY * 0.95f) { e.recover = t; break; }
        }
        Debug.Log($"AnimAnalyzer: GettingUp lying={e.impact:0.00} upright={e.recover:0.00} standHips={standY:0.00}");
    }

    // Paired throw: find the frame where the thrower's hands close on the victim's chest and the offset
    // that puts the victim there, so the two halves line up whatever origin they were exported with.
    static void AnalysePair(AnimLibrary lib, GameObject aggressorGo, string aggressor, string victim)
    {
        var ea = lib.entries.FirstOrDefault(e => e.name == aggressor);
        var ev = lib.entries.FirstOrDefault(e => e.name == victim);
        if (ea == null || ev == null) return;
        var victimGo = Object.Instantiate(aggressorGo);
        victimGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var aa = aggressorGo.GetComponent<Animator>();
        var va = victimGo.GetComponent<Animator>();
        va.applyRootMotion = false;
        va.Rebind();
        var g = PlayableGraph.Create("pair");
        g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var pa = AnimationClipPlayable.Create(g, ea.clip);
        var pv = AnimationClipPlayable.Create(g, ev.clip);
        pa.SetApplyFootIK(false); pv.SetApplyFootIK(false);
        AnimationPlayableOutput.Create(g, "a", aa).SetSourcePlayable(pa);
        AnimationPlayableOutput.Create(g, "v", va).SetSourcePlayable(pv);
        float best = float.MaxValue, bestT = 0;
        Vector3 bestOffset = Vector3.zero;
        float end = Mathf.Min(ea.length, ev.impact > 0 ? ev.impact : ev.length);
        for (float t = 0; t <= end; t += 1f / 60f)
        {
            pa.SetTime(t); pv.SetTime(t);
            g.Evaluate(0);
            Vector3 hands = (aa.GetBoneTransform(HumanBodyBones.LeftHand).position + aa.GetBoneTransform(HumanBodyBones.RightHand).position) * 0.5f;
            Vector3 chest = va.GetBoneTransform(HumanBodyBones.Chest).position;
            // hands close together and both near chest height mark the grab
            float spread = Vector3.Distance(aa.GetBoneTransform(HumanBodyBones.LeftHand).position, aa.GetBoneTransform(HumanBodyBones.RightHand).position);
            float d = Mathf.Abs(hands.y - chest.y) + spread * 0.3f + t * 0.02f;
            if (d < best) { best = d; bestT = t; bestOffset = hands - chest; }
        }
        bestOffset.y = 0;
        ea.impact = bestT;
        ea.offset = bestOffset;
        Debug.Log($"AnimAnalyzer: pair {aggressor}/{victim} contact={bestT:0.00} offset={bestOffset} victimSlam={ev.impact:0.00}");
        g.Destroy();
        Object.DestroyImmediate(victimGo);
    }

    // Contact sheet: each clip as a row of frames around its key moment, Kaito facing a Brakka dummy.
    public static void RenderSheet(string outPath, string[] only = null)
    {
        const int W = 200, H = 250, Cols = 6;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Key").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.GetComponent<Renderer>().sharedMaterial = SceneBuilder.Lit("PoseFloor", new Color(0.45f, 0.42f, 0.4f), 0.1f, 0);

        var lib = AssetDatabase.LoadAssetAtPath<AnimLibrary>(LibraryPath);
        var kaito = SceneBuilder.MakeMixamoFighter("Leonard", Vector3.zero, 0, withGame: false);
        var dummy = SceneBuilder.MakeMixamoFighter("Brute", new Vector3(0, 0, 1.2f), 180, withGame: false);
        var ka = kaito.GetComponent<Animator>();
        var da = dummy.GetComponent<Animator>();
        ka.applyRootMotion = false;
        da.applyRootMotion = false;

        var graph = PlayableGraph.Create("sheet");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var ko = AnimationPlayableOutput.Create(graph, "k", ka);
        var dout = AnimationPlayableOutput.Create(graph, "d", da);
        var idle = AnimationClipPlayable.Create(graph, lib["FightIdle"].clip);
        dout.SetSourcePlayable(idle);

        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.backgroundColor = new Color(0.16f, 0.15f, 0.17f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 42;
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.transform.position = new Vector3(3.8f, 1.2f, 0.6f);
        cam.transform.LookAt(new Vector3(0, 0.95f, 0.6f));

        var entries = lib.entries.Where(e => only == null || only.Contains(e.name)).ToList();
        var sheet = new Texture2D(W * Cols, H * entries.Count, TextureFormat.RGB24, false);
        for (int y = 0; y < entries.Count; y++)
        {
            var e = entries[y];
            var p = AnimationClipPlayable.Create(graph, e.clip);
            p.SetApplyFootIK(false);
            ko.SetSourcePlayable(p);
            float k = e.impact > 0 ? e.impact : e.length * 0.5f;
            float[] times = { 0, k * 0.5f, k * 0.85f, k, Mathf.Min(e.length, k + 0.2f), e.length };
            for (int x = 0; x < Cols; x++)
            {
                p.SetTime(times[x]);
                idle.SetTime(0.3f);
                graph.Evaluate();
                cam.Render();
                RenderTexture.active = rt;
                var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
                tile.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tile.Apply();
                var px = tile.GetPixels();
                Object.DestroyImmediate(tile);
                for (int i = 0; i < W; i++) { px[i] = Color.black; px[i + W] = Color.black; }
                for (int i = 0; i < H; i++) { px[i * W] = Color.black; px[i * W + 1] = Color.black; }
                if (x == 3) for (int i = 0; i < W; i++) { px[(H - 1) * W + i] = Color.red; px[(H - 2) * W + i] = Color.red; px[(H - 3) * W + i] = Color.red; }
                sheet.SetPixels(x * W, (entries.Count - 1 - y) * H, W, H, px);
            }
        }
        graph.Destroy();
        sheet.Apply();
        File.WriteAllBytes(outPath, sheet.EncodeToPNG());
        Debug.Log($"AnimAnalyzer: sheet {Path.GetFullPath(outPath)} rows={string.Join(",", entries.Select(e => e.name))}");
    }

    public static void BuildBatch()
    {
        try
        {
            MixamoImport.ExtractTextures();
            Build();
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-sheet");
            if (i >= 0)
            {
                string[] only = null;
                int j = System.Array.IndexOf(args, "-only");
                if (j >= 0) only = args[j + 1].Split(',');
                RenderSheet(args[i + 1], only);
            }
        }
        catch (System.Exception e) { Debug.LogError("AnimAnalyzer: FAILED " + e); EditorApplication.Exit(1); }
        EditorApplication.Exit(0);
    }
}
