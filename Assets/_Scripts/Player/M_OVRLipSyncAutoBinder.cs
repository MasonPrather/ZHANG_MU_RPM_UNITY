// Assets/_Scripts/Player/M_OVRLipSyncAutoBinder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class M_OVRLipSyncAutoBinder : MonoBehaviour
{
    [SerializeField] private OVRLipSyncContextMorphTarget lipSyncMorph; // optional, auto-gets if null

    // Call after the RPM avatar is loaded and you have its face SkinnedMeshRenderer
    public void Bind(SkinnedMeshRenderer faceMesh)
    {
        if (!faceMesh) { Debug.LogWarning("[LipSyncBinder] No face mesh provided."); return; }

        if (!lipSyncMorph) lipSyncMorph = GetComponent<OVRLipSyncContextMorphTarget>();
        if (!lipSyncMorph) lipSyncMorph = gameObject.AddComponent<OVRLipSyncContextMorphTarget>();

        lipSyncMorph.skinnedMeshRenderer = faceMesh;

        var mesh = faceMesh.sharedMesh;
        if (!mesh) { Debug.LogWarning("[LipSyncBinder] Face mesh has no sharedMesh."); return; }

        var nameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            var raw = mesh.GetBlendShapeName(i);
            nameToIndex[Normalize(raw)] = i;
        }

        // OVR viseme order (15)
        string[] ovr = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };

        int[] map = new int[ovr.Length];
        for (int i = 0; i < ovr.Length; i++)
        {
            map[i] = FindFirstIndex(nameToIndex, new[]
            {
                $"viseme_{ovr[i]}",
                $"v_{ovr[i]}",
                $"{ovr[i]}",
                $"viseme_{ovr[i].ToLower()}",
                $"{ovr[i].ToLower()}"
            });
        }
        lipSyncMorph.visemeToBlendTargets = map;

        // Laughter target (optional)
        lipSyncMorph.laughterBlendTarget = FindFirstIndex(nameToIndex, new[]
        {
            "laughter","mouthSmile","mouthSmile_L","mouthSmile_R","smile","smileOpen"
        });

        // Defaults (like your inspector)
        lipSyncMorph.laughterThreshold = 0.5f;
        lipSyncMorph.laughterMultiplier = 1.5f;
        lipSyncMorph.smoothAmount = 70;

        DebugReport(lipSyncMorph, mesh, ovr);
    }

    static string Normalize(string s)
    {
        s = s.Replace('.', '_');
        return new string(s.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
    }

    static int FindFirstIndex(Dictionary<string, int> dict, IEnumerable<string> candidates)
    {
        foreach (var c in candidates)
        {
            var key = Normalize(c);
            if (dict.TryGetValue(key, out int idx)) return idx;
        }
        return -1;
    }

    static void DebugReport(OVRLipSyncContextMorphTarget ctx, Mesh mesh, string[] ovr)
    {
        if (!Application.isEditor) return;
        for (int i = 0; i < ctx.visemeToBlendTargets.Length; i++)
        {
            int bi = ctx.visemeToBlendTargets[i];
            string name = (bi >= 0 && bi < mesh.blendShapeCount) ? mesh.GetBlendShapeName(bi) : "—";
            Debug.Log($"[LipSyncBinder] {ovr[i],3} → index {bi} ({name})");
        }
        Debug.Log($"[LipSyncBinder] laughter → {ctx.laughterBlendTarget}");
    }
}