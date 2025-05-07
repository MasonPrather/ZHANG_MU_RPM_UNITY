using UnityEngine;
using Unity.XR.CoreUtils;

public class M_XRPlayerAutoAlign : MonoBehaviour
{
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform avatarHeadTarget;

    [SerializeField] private bool alignOnStart = true;
    [SerializeField] private UnityEngine.Vector3 axisLock = new UnityEngine.Vector3(1, 0, 1);

    void Start()
    {
        if (alignOnStart)
        {
            Invoke(nameof(AlignToAvatarHead), 0.1f);
        }
    }

    void AlignToAvatarHead()
    {
        if (xrOrigin == null || avatarHeadTarget == null || xrOrigin.Camera == null)
        {
            Debug.LogWarning("[M_XRPlayerAutoAlign] Missing references!");
            return;
        }

        UnityEngine.Vector3 hmdPosition = xrOrigin.Camera.transform.position;
        UnityEngine.Vector3 offset = avatarHeadTarget.position - hmdPosition;
        offset = UnityEngine.Vector3.Scale(offset, axisLock);

        xrOrigin.MoveCameraToWorldLocation(hmdPosition + offset);

        Debug.Log($"[M_XRPlayerAutoAlign] XR Rig moved to align headset. Offset: {offset:F3}");
    }
}