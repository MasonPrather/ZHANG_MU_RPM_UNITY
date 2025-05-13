using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using static OVRFaceExpressions;
using Unity.Collections;

public class M_OVR_NetPlayer : NetworkBehaviour
{
    [Header("Tracked Transforms")]
    public Transform head;

    public Transform hips;

    public Transform leftHand;
    public Transform rightHand;
    /*
    public Transform leftFoot;
    public Transform rightFoot;
    public Transform spine;
    public Transform spine1;
    public Transform spine2;
    */

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
    /*
    private NetworkVariable<Vector3> syncedLeftFootPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedLeftFootRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedRightFootPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedRightFootRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<Vector3> syncedSpinePos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedSpineRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<Vector3> syncedSpine1Pos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedSpine1Rot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> syncedSpine2Pos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> syncedSpine2Rot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    */
    private NetworkList<float> syncedExpressionWeights = new NetworkList<float>();

    private readonly FaceExpression[] expressionsToTrack = new FaceExpression[]
    {
        FaceExpression.JawDrop,
        FaceExpression.LipCornerPullerL,
        FaceExpression.LipCornerPullerR,
        FaceExpression.EyesClosedL,
        FaceExpression.EyesClosedR
    };

    private readonly string[] blendShapeNames = new string[]
    {
        "jawOpen",
        "mouthSmileLeft",
        "mouthSmileRight",
        "eyeBlinkLeft",
        "eyeBlinkRight"
    };

    void Start()
    {
        if (IsOwner)
        {
            foreach (var item in meshToDisable)
                item.enabled = false;
        }

        if (IsServer)
        {
            syncedExpressionWeights.Clear();
            foreach (var _ in expressionsToTrack)
                syncedExpressionWeights.Add(0f);
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

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
        if (rig == null || rig.head == null || rig.leftHand == null || rig.rightHand == null || rig.hips == null || rig.faceExpressions == null)
            return;

        syncedHeadPos.Value = rig.head.position;
        syncedHeadRot.Value = rig.head.rotation;

        syncedLeftHandPos.Value = rig.leftHand.position;
        syncedLeftHandRot.Value = rig.leftHand.rotation;

        syncedRightHandPos.Value = rig.rightHand.position;
        syncedRightHandRot.Value = rig.rightHand.rotation;
        
        syncedHipsPos.Value = rig.hips.position;
        syncedHipsRot.Value = rig.hips.rotation;
        /*
        syncedLeftFootPos.Value = rig.leftFoot.position;
        syncedLeftFootRot.Value = rig.leftFoot.rotation;

        syncedRightFootPos.Value = rig.rightFoot.position;
        syncedRightFootRot.Value = rig.rightFoot.rotation;
        
        syncedSpinePos.Value = rig.spine.position;
        syncedSpineRot.Value = rig.spine.rotation;
        
        syncedSpine1Pos.Value = rig.spine1.position;
        syncedSpine1Rot.Value = rig.spine1.rotation;

        syncedSpine2Pos.Value = rig.spine2.position;
        syncedSpine2Rot.Value = rig.spine2.rotation;
        */
        for (int i = 0; i < expressionsToTrack.Length; i++)
        {
            float weight = rig.faceExpressions.GetWeight(expressionsToTrack[i]);
            if (i < syncedExpressionWeights.Count)
                syncedExpressionWeights[i] = weight;
        }
    }

    private void ApplySyncData()
    {
        if (head != null) head.SetPositionAndRotation(syncedHeadPos.Value, syncedHeadRot.Value);
        if (leftHand != null) leftHand.SetPositionAndRotation(syncedLeftHandPos.Value, syncedLeftHandRot.Value);
        if (rightHand != null) rightHand.SetPositionAndRotation(syncedRightHandPos.Value, syncedRightHandRot.Value);
        if (hips != null) hips.SetPositionAndRotation(syncedHipsPos.Value, syncedHipsRot.Value);
        /*
        if (leftFoot != null) leftFoot.SetPositionAndRotation(syncedLeftFootPos.Value, syncedLeftFootRot.Value);
        if (rightFoot != null) rightFoot.SetPositionAndRotation(syncedRightFootPos.Value, syncedRightFootRot.Value);
        if (spine != null) spine.SetPositionAndRotation(syncedSpinePos.Value, syncedSpineRot.Value);
        if (spine1 != null) spine1.SetPositionAndRotation(syncedSpine1Pos.Value, syncedSpine1Rot.Value);
        if (spine2 != null) spine2.SetPositionAndRotation(syncedSpine2Pos.Value, syncedSpine2Rot.Value);
        */
    }

    private void ApplyFacialExpressions()
    {
        if (avatarMesh == null || syncedExpressionWeights == null || syncedExpressionWeights.Count != blendShapeNames.Length)
            return;

        for (int i = 0; i < blendShapeNames.Length; i++)
        {
            int index = avatarMesh.sharedMesh.GetBlendShapeIndex(blendShapeNames[i]);
            if (index >= 0)
                avatarMesh.SetBlendShapeWeight(index, syncedExpressionWeights[i] * 100f);
        }
    }

    private void OnDestroy()
    {
        if (IsServer && syncedExpressionWeights != null)
        {
            syncedExpressionWeights.Dispose();
        }
    }
}