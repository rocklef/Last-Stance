using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Mixamo FBX materials come in without usable textures, so rebuild them as URP Lit materials from
// the extracted maps (ChNN_1001_* body, ChNN_1002_* hair, or <Part>_Diffuse/_Normal/_Opacity).
// Emission is enabled (black) so the hit flash can tint the whole body through a property block.
public static class MixamoMaterials
{
    const string OutDir = "Assets/Generated/Characters";
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static void Apply(GameObject instance, string characterName)
    {
        Directory.CreateDirectory(OutDir);
        string texDir = MixamoImport.CharDir + "Textures/" + characterName;
        var files = Directory.Exists(texDir)
            ? Directory.GetFiles(texDir).Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".tga")).Select(p => p.Replace('\\', '/')).ToArray()
            : new string[0];
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            // Brute ships holding an axe; this is a bare-knuckle fight
            if (r.name.ToLower().Contains("moustache") || r.name.ToLower().Contains("axe") || r.sharedMaterials.Any(m => m && m.name.ToLower().Contains("axe")))
            {
                Debug.Log($"MixamoMaterials: hiding prop {r.name}");
                Object.DestroyImmediate(r.gameObject);
                continue;
            }
            Debug.Log($"MixamoMaterials: renderer {r.name} mats={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}");
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i]) mats[i] = Build(characterName, files, mats[i]);
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    static readonly string[] Parts = { "body", "bottom", "top", "hair", "moustache", "beard", "shoes", "axe", "lash", "eyespec", "eye" };

    static string N(string f) => Path.GetFileName(f).ToLower();

    static Material Build(string character, string[] files, Material src)
    {
        string matName = src.name;
        string key = character + "|" + matName;
        if (cache.TryGetValue(key, out var existing) && existing) return existing;

        string lname = matName.ToLower();
        string part = Parts.FirstOrDefault(k => lname.Contains(k));
        bool hairLike = part == "hair" || part == "moustache" || part == "beard" || part == "lash";
        bool invisible = part == "eyespec" || part == "lash";

        // colour map: a file naming this body part, else Mixamo's numbered 1001 (body) / 1002 (hair) maps
        string diffuse = part == null ? null :
            files.FirstOrDefault(f => N(f).Contains(part) && (N(f).Contains("diffuse") || N(f).Contains("_dif")) && N(f).EndsWith(".png")) ??
            files.FirstOrDefault(f => N(f).Contains(part) && (N(f).Contains("diffuse") || N(f).Contains("_dif")));
        if (diffuse == null)
            diffuse = hairLike ? Find(files, "_1002_", "diffuse") : Find(files, "_1001_", "diffuse") ?? Find(files, "", "diffuse");
        string normal = null, opacity = null;
        if (diffuse != null)
        {
            string stem = N(diffuse);
            normal = part != null ? files.FirstOrDefault(f => N(f).Contains(part) && (N(f).Contains("normal") || N(f).Contains("_nm"))) : null;
            if (normal == null && part == "body") normal = files.FirstOrDefault(f => N(f).Contains("stdnm"));
            if (normal == null)
            {
                string guess = stem.Replace("diffuse", "normal");
                normal = files.FirstOrDefault(f => N(f) == guess && N(f) != stem);
            }
            string op = stem.Replace("diffuse", "opacity");
            opacity = files.FirstOrDefault(f => N(f) == op && N(f) != stem);
        }

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = matName };
        mat.SetFloat("_Smoothness", hairLike ? 0.3f : 0.34f);
        mat.SetColor("_BaseColor", Color.white);
        if (diffuse != null)
        {
            var dti = (TextureImporter)AssetImporter.GetAtPath(diffuse);
            if (dti.textureType == TextureImporterType.NormalMap) { dti.textureType = TextureImporterType.Default; dti.SaveAndReimport(); }
            var baseMap = opacity != null && hairLike ? MergeAlpha(diffuse, opacity) : AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse);
            mat.SetTexture("_BaseMap", baseMap);
            bool hasAlpha = opacity != null || dti.DoesSourceTextureHaveAlpha();
            if (hairLike && hasAlpha)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.35f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);
            }
        }
        if (normal != null)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(normal);
            if (ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            mat.EnableKeyword("_NORMALMAP");
        }
        if (invisible)
        {
            // eye-specular shells and lash cards have no maps of their own; hide them
            mat.SetFloat("_Surface", 1);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetColor("_BaseColor", new Color(1, 1, 1, 0));
            mat.SetTexture("_BaseMap", null);
        }
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.black);

        string path = $"{OutDir}/{character}_{matName}.mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (old != null)
        {
            EditorUtility.CopySerialized(mat, old);
            old.shaderKeywords = mat.shaderKeywords;
            EditorUtility.SetDirty(old);
            mat = old;
        }
        else AssetDatabase.CreateAsset(mat, path);
        cache[key] = mat;
        Debug.Log($"MixamoMaterials: {character}/{matName} part={part ?? "-"} diffuse={(diffuse != null ? Path.GetFileName(diffuse) : "none")} normal={(normal != null ? Path.GetFileName(normal) : "none")}{(invisible ? " hidden" : "")}");
        return mat;
    }

    static string Find(string[] files, string part, string kind) =>
        files.FirstOrDefault(f => Path.GetFileName(f).ToLower().Contains(part) && Path.GetFileName(f).ToLower().Contains(kind));

    static Texture2D MergeAlpha(string diffusePath, string opacityPath)
    {
        string outPath = $"{OutDir}/{Path.GetFileNameWithoutExtension(diffusePath)}_RGBA.png";
        if (!File.Exists(outPath))
        {
            var c = new Texture2D(2, 2); c.LoadImage(File.ReadAllBytes(diffusePath));
            var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(opacityPath));
            var px = c.GetPixels32();
            var ax = a.width == c.width && a.height == c.height ? a.GetPixels32() : null;
            for (int i = 0; i < px.Length; i++) px[i].a = ax != null ? ax[i].r : (byte)255;
            var outTex = new Texture2D(c.width, c.height, TextureFormat.RGBA32, false);
            outTex.SetPixels32(px);
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }
}
