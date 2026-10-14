using System.Linq;
using UnityEditor;
using UnityEngine;

// Diagnostic: lists each skinned mesh's root bone and the bones it is bound to, per Mixamo character.
public static class RigDiag
{
    public static void Run()
    {
        foreach (var name in new[] { "Leonard", "Brute" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(MixamoImport.CharDir + name + ".fbx");
            var anim = go.GetComponent<Animator>();
            Debug.Log($"RigDiag: {name} avatar={(anim && anim.avatar ? anim.avatar.name + " human=" + anim.avatar.isHuman : "none")}");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                int nulls = bones.Count(b => b == null);
                var feet = bones.Where(b => b && (b.name.Contains("Foot") || b.name.Contains("Toe"))).Select(b => b.name);
                Debug.Log($"RigDiag: {name}/{smr.name} root={(smr.rootBone ? smr.rootBone.name : "null")} bones={bones.Length} nullBones={nulls} feet=[{string.Join(",", feet)}] verts={smr.sharedMesh.vertexCount} bw={smr.sharedMesh.boneWeights.Length}");
            }
        }
        EditorApplication.Exit(0);
    }
}
