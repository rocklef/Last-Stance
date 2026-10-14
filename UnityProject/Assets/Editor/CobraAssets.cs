using UnityEditor;
using UnityEngine;

// The King Cobra's materials, built from the textures tools/cobra/paint_cobra.py paints into
// Assets/Resources/Cobra (albedo with smoothness in alpha, tangent-space normals, ember emission).
// Kept as assets under Resources so the player build keeps exactly these shader variants.
public static class CobraAssets
{
    const string Dir = "Assets/Resources/Cobra";

    [MenuItem("Last Stance/Build Cobra Materials")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) { Debug.LogWarning("CobraAssets: no " + Dir); return; }
        Lit("CobraBody", "Body", 0.95f, 1.1f, new Color(1.6f, 1.6f, 1.6f));
        Lit("CobraHood", "Hood", 0.95f, 1.0f, new Color(2.2f, 2.2f, 2.2f));
        Lit("CobraHead", "Head", 0.95f, 1.0f, new Color(1.4f, 1.4f, 1.4f));
        Lit("CobraMouth", "Mouth", 1f, 0.6f, new Color(3.5f, 3.5f, 3.5f));
        Lit("CobraEye", "Eye", 1f, 0f, new Color(5f, 5f, 5f));
        Lit("StonePlates", "Plates", 1f, 1.2f, new Color(1.8f, 1.8f, 1.8f));   // the stone ball's core, the wall's blocks
        var plates = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/StonePlates.mat");
        if (plates) { plates.enableInstancing = true; EditorUtility.SetDirty(plates); }
        Plain("CobraFang", new Color(0.86f, 0.82f, 0.72f), 0.7f);
        Plain("CobraTongue", new Color(0.16f, 0.025f, 0.035f), 0.85f);
        AssetDatabase.SaveAssets();
        Debug.Log("CobraAssets: materials built");
    }

    static Texture2D Tex(string name, bool normal)
    {
        string path = $"{Dir}/{name}.png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        bool dirty = false;
        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
        if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
        if (!normal && ti.alphaIsTransparency) { ti.alphaIsTransparency = false; dirty = true; }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Fresh(string name)
    {
        string path = $"{Dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    static void Lit(string name, string tex, float smooth, float bump, Color emission)
    {
        var m = Fresh(name);
        m.SetColor("_BaseColor", Color.white);
        var a = Tex(tex + "_A", false);
        if (a)
        {
            m.SetTexture("_BaseMap", a);
            m.SetFloat("_SmoothnessTextureChannel", 1);   // smoothness lives in the albedo's alpha
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        }
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        var n = Tex(tex + "_N", true);
        if (n && bump > 0)
        {
            m.SetTexture("_BumpMap", n);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        var e = Tex(tex + "_E", false);
        if (e)
        {
            m.SetTexture("_EmissionMap", e);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(m);
    }

    static void Plain(string name, Color c, float smooth)
    {
        var m = Fresh(name);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
    }
}
