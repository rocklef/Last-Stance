using System.IO;
using System.Linq;
using UnityEditor;

// Mixamo FBX files under Assets/Mixamo import as humanoids so every clip retargets onto both fighters.
//   Assets/Mixamo/Characters/*.fbx  skinned characters (textures extracted next to them)
//   Assets/Mixamo/Anims/*.fbx       skeleton-only mocap clips, one clip per file named after the file
// Root rotation and height are baked into the pose (spins and falls play as authored); horizontal root
// motion is extracted so the fighter code decides where the body goes, except for the paired throw,
// whose two halves are authored around a shared origin and must keep their offsets.
public class MixamoImport : AssetPostprocessor
{
    public const string Root = "Assets/Mixamo/";
    public const string CharDir = Root + "Characters/";
    public const string AnimDir = Root + "Anims/";

    static readonly string[] Loops = { "FightIdle", "BouncingIdle", "StepForward", "StepBackward", "StrafeLeft", "StrafeRight", "Run", "CrouchIdle", "Casting", "Running", "FastRun", "FallingIdle" };
    static readonly string[] KeepOffsets = { };   // every clip stays centred on its root

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Root)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = assetPath.StartsWith(CharDir) ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
        if (assetPath.StartsWith(AnimDir)) importer.importBlendShapes = false;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(AnimDir)) return;
        var importer = (ModelImporter)assetImporter;
        string name = Path.GetFileNameWithoutExtension(assetPath);
        var src = importer.defaultClipAnimations;
        if (src.Length == 0) return;
        var clip = src[0];
        clip.name = name;
        clip.loopTime = Loops.Contains(name);
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        bool keep = KeepOffsets.Contains(name);
        clip.lockRootPositionXZ = keep;
        clip.keepOriginalPositionXZ = keep;
        if (name == "DodgeRight")
        {
            var left = new ModelImporterClipAnimation
            {
                name = "DodgeLeft", takeName = clip.takeName, firstFrame = clip.firstFrame, lastFrame = clip.lastFrame, mirror = true,
                lockRootRotation = true, keepOriginalOrientation = true, lockRootHeightY = true, keepOriginalPositionY = true,
            };
            importer.clipAnimations = new[] { clip, left };
        }
        else importer.clipAnimations = new[] { clip };
    }

    // Mixamo embeds skin and clothing textures in the FBX; pull them out so materials can use them.
    public static void ExtractTextures()
    {
        if (!Directory.Exists(CharDir)) return;
        foreach (var file in Directory.GetFiles(CharDir, "*.fbx"))
        {
            string path = file.Replace('\\', '/');
            string texDir = CharDir + "Textures/" + Path.GetFileNameWithoutExtension(path);
            if (Directory.Exists(texDir) && Directory.GetFiles(texDir).Length > 0) continue;
            Directory.CreateDirectory(texDir);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.ExtractTextures(texDir);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
