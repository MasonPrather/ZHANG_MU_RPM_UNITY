using UnityEngine;

public class M_HideHeadOneShot : MonoBehaviour
{
    [Tooltip("Path from avatar root to head bone (leave empty to search by name)")]
    public string headBonePath = "Armature/Hips/Spine/Spine1/Spine2/Neck/Head";

    private bool hasHidden = false;

    public void DisableHead()
    {
        if (hasHidden) return;
        hasHidden = true;

        Transform head = string.IsNullOrEmpty(headBonePath)
            ? FindChildByName(transform, "Head")
            : transform.Find(headBonePath);

        if (head == null)
        {
            Debug.LogWarning($"[M_HideHeadOneShot] Could not find head at path: {headBonePath}");
            return;
        }

        foreach (var renderer in head.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        Debug.Log("[M_HideHeadOneShot] Head renderers disabled.");
    }

    private Transform FindChildByName(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }
        return null;
    }
}