using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Pose replication for head + hands.
/// Owner writes XR-anchor poses to NetworkVariables; remotes read them and
/// place the IKTargets (assigned in the NetPlayer prefab).
/// </summary>
public class M_NetPoseDriver : NetworkBehaviour
{
    [Header("Tracked Targets (this prefab's IKTargets)")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Header("Owner XR Anchors (auto-found if empty for the Owner only)")]
    public Transform headSrc;
    public Transform leftHandSrc;
    public Transform rightHandSrc;

    [Header("Remote Smoothing")]
    [Range(0f, 1f)] public float lerp = 0.25f;

    // NetworkVariables (Owner write → Everyone read)
    public readonly NetworkVariable<Vector3> headPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<Quaternion> headRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public readonly NetworkVariable<Vector3> leftPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<Quaternion> leftRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public readonly NetworkVariable<Vector3> rightPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<Quaternion> rightRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> poseReady = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool HasRemotePose { get; private set; }

    private void Awake()
    {
        poseReady.OnValueChanged += (_, now) => { HasRemotePose = now; };
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) AutoFindOVRAnchors();
    }

    private void Update()
    {
        if (IsOwner)
        {
            // Publish
            if (headSrc) { headPos.Value = headSrc.position; headRot.Value = headSrc.rotation; }
            if (leftHandSrc) { leftPos.Value = leftHandSrc.position; leftRot.Value = leftHandSrc.rotation; }
            if (rightHandSrc) { rightPos.Value = rightHandSrc.position; rightRot.Value = rightHandSrc.rotation; }

            // Mark pose as ready once we have head
            if (!poseReady.Value && headSrc) poseReady.Value = true;
        }
        // Consume
        if (headTarget)
        {
            headTarget.position = Vector3.Lerp(headTarget.position, headPos.Value, lerp);
            headTarget.rotation = Quaternion.Slerp(headTarget.rotation, headRot.Value, lerp);
        }
        if (leftHandTarget)
        {
            leftHandTarget.position = Vector3.Lerp(leftHandTarget.position, leftPos.Value, lerp);
            leftHandTarget.rotation = Quaternion.Slerp(leftHandTarget.rotation, leftRot.Value, lerp);
        }
        if (rightHandTarget)
        {
            rightHandTarget.position = Vector3.Lerp(rightHandTarget.position, rightPos.Value, lerp);
            rightHandTarget.rotation = Quaternion.Slerp(rightHandTarget.rotation, rightRot.Value, lerp);
        } 
    }

    private void AutoFindOVRAnchors()
    {
        if (headSrc && leftHandSrc && rightHandSrc) return;

        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig)
        {
            if (!headSrc) headSrc = rig.centerEyeAnchor;
            if (!leftHandSrc) leftHandSrc = rig.leftControllerAnchor;
            if (!rightHandSrc) rightHandSrc = rig.rightControllerAnchor;
        }
        else
        {
            // Fallback to common names if user swaps to XR Origin someday
            var cam = GameObject.Find("Main Camera");
            var l = GameObject.Find("LeftHand Controller");
            var r = GameObject.Find("RightHand Controller");
            if (!headSrc && cam) headSrc = cam.transform;
            if (!leftHandSrc && l) leftHandSrc = l.transform;
            if (!rightHandSrc && r) rightHandSrc = r.transform;
        }
    }
}