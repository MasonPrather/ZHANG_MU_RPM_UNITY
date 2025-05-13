using UnityEngine;

public class M_HideLocalHeadMesh : MonoBehaviour
{
    private bool hasHidden = false;

    private readonly string[] rendererNamesToHide = new string[]
    {
        "Renderer_EyeLeft",
        "Renderer_EyeRight",
        "Renderer_Head",
        "Renderer_Teeth",
        "Renderer_Hair"
    };

    public void DisableHead()
    {
        if (hasHidden) return;
        hasHidden = true;

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var rend in allRenderers)
        {
            if (System.Array.Exists(rendererNamesToHide, name => rend.gameObject.name == name))
            {
                rend.enabled = false;
                Debug.Log($"[M_HideLocalHeadMesh] Disabled renderer: {rend.gameObject.name}");
            }
        }
    }
}