using UnityEngine;

public class M_AvatarController : MonoBehaviour
{
    [Header("Head and Hand Mappings")]
    [SerializeField] private MapTransforms head;
    [SerializeField] private MapTransforms leftHand;
    [SerializeField] private MapTransforms rightHand;

    [Header("Hips")]
    [SerializeField] private MapTransforms hips;

    [Header("Other Settings")]
    [SerializeField] private float turnSmoothness;
    [SerializeField] private Transform ikHead;
    [SerializeField] private Vector3 headBodyOffset;

    private void LateUpdate()
    {
        head.VRMapping();
        leftHand.VRMapping();
        rightHand.VRMapping();
        hips.VRMapping();
    }
}

[System.Serializable]
public class MapTransforms
{
    public Transform vrTarget;
    public Transform ikTarget;
    public Vector3 trackingPositionOffset;
    public Vector3 trackingRotationOffset;

    public void VRMapping()
    {
        ikTarget.position = vrTarget.TransformPoint(trackingPositionOffset);
        ikTarget.rotation = vrTarget.rotation * Quaternion.Euler(trackingRotationOffset);
    }
}