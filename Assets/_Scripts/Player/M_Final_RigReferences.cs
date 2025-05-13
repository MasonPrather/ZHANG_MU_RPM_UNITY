using UnityEngine;
using ReadyPlayerMe.Core;

public class M_Final_RigReferences : MonoBehaviour
{
    public static M_Final_RigReferences Singleton { get; private set; }

    [Header("Tracked XR Device Targets")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public Transform hips;

    [Header("Avatar Expression References")]
    public OVRFaceExpressions faceExpressions;

    private void Awake()
    {
        if (Singleton != null && Singleton != this)
        {
            Destroy(this);
            return;
        }
        Singleton = this;
    }

    public void AssignAvatarReferences(GameObject avatar)
    {
        faceExpressions = avatar.GetComponentInChildren<OVRFaceExpressions>();
    }
}
