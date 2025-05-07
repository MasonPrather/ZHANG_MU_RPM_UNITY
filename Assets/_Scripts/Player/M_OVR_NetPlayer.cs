using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using static OVRFaceExpressions;

public class M_OVR_NetPlayer : NetworkBehaviour
{
    [Header("Tracked Transforms")]
    public Transform root;
    public Transform head;
    public Transform hips;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Avatar Mesh")]
    public SkinnedMeshRenderer avatarMesh;

    [Header("Mesh to Hide on Local Player")]
    public Renderer[] meshToDisable;

    private NetworkVariable<Vector3> syncedHeadPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedHeadRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedLeftHandPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedLeftHandRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedRightHandPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedRightHandRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedHipsPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedHipsRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedEyeForward = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Dictionary<FaceExpression, float> syncedExpressions = new();

    private Dictionary<FaceExpression, string> expressionToBlendshape = new()
    {
        { FaceExpression.JawDrop, "jawOpen" },
        { FaceExpression.LipCornerPullerL, "mouthSmileLeft" },
        { FaceExpression.LipCornerPullerR, "mouthSmileRight" },
        { FaceExpression.EyesClosedL, "eyeBlinkLeft" },
        { FaceExpression.EyesClosedR, "eyeBlinkRight" }
    };

    private void Start()
    {
        if (IsOwner)
        {
            foreach (var item in meshToDisable)
                item.enabled = false;
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            UpdateSyncData();
        }
        else
        {
            ApplySyncData();
            ApplyFacialExpressions();
        }
    }

    private void UpdateSyncData()
    {
        var rig = M_OVR_RigReferences.Singleton;

        syncedHeadPos.Value = rig.head.position;
        syncedHeadRot.Value = rig.head.rotation;

        syncedLeftHandPos.Value = rig.leftHand.position;
        syncedLeftHandRot.Value = rig.leftHand.rotation;

        syncedRightHandPos.Value = rig.rightHand.position;
        syncedRightHandRot.Value = rig.rightHand.rotation;

        syncedHipsPos.Value = rig.hips.position;
        syncedHipsRot.Value = rig.hips.rotation;


        if (rig.faceExpressions != null)
        {
            foreach (var kvp in expressionToBlendshape)
            {
                var exp = kvp.Key;
                float weight = rig.faceExpressions.GetWeight(exp);
                syncedExpressions[exp] = weight;
            }
        }
    }

    private void ApplySyncData()
    {
        head.SetPositionAndRotation(syncedHeadPos.Value, syncedHeadRot.Value);
        leftHand.SetPositionAndRotation(syncedLeftHandPos.Value, syncedLeftHandRot.Value);
        rightHand.SetPositionAndRotation(syncedRightHandPos.Value, syncedRightHandRot.Value);
        hips.SetPositionAndRotation(syncedHipsPos.Value, syncedHipsRot.Value);
    }

    private void ApplyFacialExpressions()
    {
        if (avatarMesh == null) return;

        foreach (var kvp in expressionToBlendshape)
        {
            if (!syncedExpressions.TryGetValue(kvp.Key, out float weight))
                continue;

            int index = avatarMesh.sharedMesh.GetBlendShapeIndex(kvp.Value);
            if (index < 0) continue;

            avatarMesh.SetBlendShapeWeight(index, weight * 100f);
        }
    }
}
