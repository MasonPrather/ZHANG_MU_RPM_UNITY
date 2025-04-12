using UnityEngine;
using UnityEditor;
using System.IO;

public class ReplaceGlTFastMaterials : Editor
{
    [MenuItem("Tools/Replace glTFast Materials with URP Lit")]
    static void ReplaceMaterials()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int replacedCount = 0;

        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null) continue;

            // Check if using glTFast shader
            if (mat.shader.name.Contains("glTF") && mat.shader.name.Contains("pbrMetallicRoughness"))
            {
                Texture baseColor = mat.HasProperty("_BaseColorTexture") ? mat.GetTexture("_BaseColorTexture") : null;
                Texture metallicRoughness = mat.HasProperty("_MetallicRoughnessTexture") ? mat.GetTexture("_MetallicRoughnessTexture") : null;

                // Replace shader
                mat.shader = Shader.Find("Universal Render Pipeline/Lit");

                // Reassign texture maps
                if (baseColor != null)
                    mat.SetTexture("_BaseMap", baseColor);

                if (metallicRoughness != null)
                    mat.SetTexture("_MetallicGlossMap", metallicRoughness);

                // Enable metallic map usage
                mat.SetFloat("_Smoothness", 0.5f); // Default guess
                mat.EnableKeyword("_METALLICGLOSSMAP");

                EditorUtility.SetDirty(mat);
                replacedCount++;
                Debug.Log($"Replaced: {path}");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Replaced {replacedCount} glTFast materials with URP Lit.");
    }
}