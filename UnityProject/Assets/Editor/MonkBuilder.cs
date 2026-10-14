using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds MOROS, THE SHARD MONK: the Mixamo "Leonard" body recast as a pale carved-stone monk
// (no hair, ember eyes), an animator with his mocap states, and the instanced shard materials
// his swarm is drawn with.
public static class MonkBuilder
{
    const string Dir = "Assets/Generated/Moros";

    public static void Build(GameManager gm)
    {
        Directory.CreateDirectory(Dir);
        var stone = Lit("MorosStone", new Color(0.86f, 0.84f, 0.8f), 0.42f);
        stone.EnableKeyword("_EMISSION");   // lets the boss make his body smoulder through a property block
        stone.SetColor("_EmissionColor", Color.black);
        stone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        var eye = Lit("MorosEye", new Color(1f, 0.25f, 0.08f), 0.6f);
        eye.EnableKeyword("_EMISSION");
        eye.SetColor("_EmissionColor", new Color(6f, 0.9f, 0.2f));
        eye.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        var shardStone = Lit("ShardStone", new Color(0.72f, 0.66f, 0.57f), 0.15f);
        var shardTile = Lit("ShardTile", new Color(0.62f, 0.12f, 0.07f), 0.25f);
        var shardIron = Lit("ShardIron", new Color(0.19f, 0.25f, 0.35f), 0.3f);   // the titan's painted-iron straps and plates
        var shardEmber = Lit("ShardEmber", new Color(1f, 0.35f, 0.1f), 0.3f);
        var shardSteel = Lit("ShardSteel", new Color(0.44f, 0.45f, 0.48f), 0.76f);   // the giant's gunmetal
        shardSteel.SetFloat("_Metallic", 0.85f);
        shardEmber.EnableKeyword("_EMISSION");
        shardEmber.SetColor("_EmissionColor", new Color(5f, 1.1f, 0.25f));
        Textured(shardStone, "japanese_stone_wall", 0.45f);
        Textured(shardTile, "grey_roof_tiles", 0.6f);
        Textured(shardIron, "japanese_stone_wall", 0.45f);
        Textured(shardSteel, "japanese_stone_wall", 0.3f);
        shardIron.SetFloat("_Metallic", 0.35f);
        foreach (var m in new[] { shardStone, shardTile, shardEmber, shardIron, shardSteel }) { m.enableInstancing = true; EditorUtility.SetDirty(m); }

        var warn = Lit("MorosWarn", new Color(0.9f, 0.08f, 0.04f), 0.2f);
        warn.EnableKeyword("_EMISSION");
        warn.SetColor("_EmissionColor", new Color(3f, 0.25f, 0.08f));

        gm.monkPrefab = BuildPrefab(stone, eye);
        gm.shardMats = new[] { shardStone, shardTile, shardEmber, shardIron, shardSteel };
        gm.warnMat = warn;

        // Kaito's powers: the Guardian Spirit (his own body as a giant of blue light) and the
        // blue fire his hands burn with during the Blade Hand Awakening
        var spirit = Glow("SpiritLight", new Color(0.28f, 0.62f, 1.7f, 0.42f));
        gm.glowMat = Glow("AwakenGlow", new Color(0.55f, 0.9f, 2.4f, 0.9f));
        gm.guardianPrefab = BuildGuardian(spirit);
        AssetDatabase.SaveAssets();
    }

    static GameObject BuildPrefab(Material stone, Material eye)
    {
        var go = SceneBuilder.MakeMixamoFighter("Leonard", Vector3.zero, 0, false);
        go.name = "Moros";
        go.transform.localScale = Vector3.one * 1.15f;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name.ToLower();
            bool hair = n.Contains("hair") || n.Contains("lash") || n.Contains("brow") || n.Contains("beard");
            foreach (var m in r.sharedMaterials) if (m && (m.name.ToLower().Contains("hair") || m.name.ToLower().Contains("lash"))) hair = true;
            if (hair) { Object.DestroyImmediate(r.gameObject); continue; }
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = stone;
            r.sharedMaterials = mats;
        }

        var an = go.GetComponent<Animator>();
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        an.runtimeAnimatorController = BuildController();

        // ember eyes on the head bone
        Transform head = an.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) foreach (var tr in go.GetComponentsInChildren<Transform>()) if (tr.name.EndsWith("Head")) { head = tr; break; }
        if (head != null)
        {
            var neck = an.GetBoneTransform(HumanBodyBones.Neck);
            Vector3 fwd = go.transform.forward;
            Vector3 right = go.transform.right;
            float s = go.transform.localScale.x;
            foreach (float side in new[] { -1f, 1f })
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(e.GetComponent<Collider>());
                e.name = side < 0 ? "EyeL" : "EyeR";
                e.GetComponent<Renderer>().sharedMaterial = eye;
                e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                e.transform.position = head.position + Vector3.up * 0.075f * s + fwd * 0.095f * s + right * side * 0.033f * s;
                e.transform.localScale = new Vector3(0.028f, 0.014f, 0.012f) * s;
                e.transform.SetParent(head, true);
            }
            Debug.Log($"MonkBuilder: eyes on {head.name}");
        }
        else Debug.LogWarning("MonkBuilder: no head bone found for eyes");

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Moros.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject BuildGuardian(Material light)
    {
        var go = SceneBuilder.MakeMixamoFighter("Leonard", Vector3.zero, 0, false);
        go.name = "GuardianSpirit";
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = light;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var an = go.GetComponent<Animator>();
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var drv = go.AddComponent<AnimDriver>();
        drv.library = AssetDatabase.LoadAssetAtPath<AnimLibrary>(AnimAnalyzer.LibraryPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Dir + "/GuardianSpirit.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // additive, unlit, see-through: reads as a body made of light (HDR colour feeds the bloom)
    static Material Glow(string name, Color c)
    {
        string path = $"{Dir}/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 2);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0);
        m.SetFloat("_Cull", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static AnimatorController BuildController()
    {
        string path = Dir + "/Moros.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        var lib = AssetDatabase.LoadAssetAtPath<AnimLibrary>(AnimAnalyzer.LibraryPath);
        var states = new Dictionary<string, string>
        {
            { "Idle", "FightIdle" }, { "Bow", "Bow" }, { "Summon", "Casting" }, { "Cast", "Cross" }, { "PowerUp", "PowerUp" },
            { "Hook", "Hook" }, { "Uppercut", "Uppercut" }, { "Hit", "HitHeadStraight" }, { "Stagger", "ReactLargeFront" },
            { "Fall", "FallingBack" }, { "Victory", "VictoryCelebrate" },
        };
        AnimatorState first = null;
        foreach (var kv in states)
        {
            var e = lib != null ? lib[kv.Value] : null;
            if (e == null || e.clip == null) { Debug.LogWarning($"MonkBuilder: missing clip {kv.Value}"); continue; }
            var st = sm.AddState(kv.Key);
            st.motion = e.clip;
            if (kv.Key == "Cast" || kv.Key == "Hook" || kv.Key == "Uppercut") st.speed = 1.3f;
            if (kv.Key == "Idle") first = st;
        }
        if (first != null) sm.defaultState = first;
        return ctrl;
    }

    // stone grain on every chip: the texture tinted by the material colour, plus its normal map
    static void Textured(Material m, string tex, float tiling)
    {
        string b = $"Assets/External/textures/{tex}/{tex}";
        var dif = AssetDatabase.LoadAssetAtPath<Texture2D>(b + "_Diffuse.jpg");
        var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(b + "_nor_gl.jpg");
        // surface relief only: the albedo keeps the chip's own colour (a dark wall texture made the swarm look like soot)
        m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        if (nrm)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(b + "_nor_gl.jpg");
            if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(b + "_nor_gl.jpg"); }
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", 1.2f);
            m.EnableKeyword("_NORMALMAP");
        }
    }

    static Material Lit(string name, Color c, float smooth)
    {
        string path = $"{Dir}/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
