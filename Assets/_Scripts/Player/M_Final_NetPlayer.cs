using Unity.Netcode;
using UnityEngine;
using RootMotion.FinalIK;
using System.Collections.Generic;
using static OVRFaceExpressions;

public class M_Final_NetPlayer : NetworkBehaviour
{
    [Header("IK Target Transforms")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform pelvisTarget;

    [Header("Final IK Component")]
    public VRIK vrik;

    [Header("Avatar Mesh")]
    public SkinnedMeshRenderer avatarMesh;

    [Header("Mesh to Hide on Local Player")]
    public Renderer[] meshToDisable;

    [Header("Eye Target (Optional)")]
    public Transform eyeLookTarget;

    private readonly NetworkVariable<Vector3> syncedHeadPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedHeadRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> syncedLeftHandPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedLeftHandRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> syncedRightHandPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedRightHandRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> syncedHipsPos = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> syncedHipsRot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> syncedEyeForward = new(Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkList<float> syncedExpressionWeights = new();

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

    private void Start()
    {
        if (IsOwner)
        {
            foreach (var mesh in meshToDisable)
            {
                if (mesh != null)
                    mesh.enabled = false;
            }
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

        ApplySyncData();

        if (IsOwner)
        {
            UpdateSyncData();
        }
        else
        {
            ApplyFacialExpressions();
            ApplyEyeTracking();
        }
    }

    private void UpdateSyncData()
    {
        var rig = M_Final_RigReferences.Singleton;
        if (rig == null || rig.head == null || rig.leftHand == null || rig.rightHand == null || rig.hips == null || !rig.IsFaceTrackingValid() || !rig.IsEyeTrackingValid())
        {
            Debug.LogWarning("[NetPlayer] Rig reference or tracking is missing.");
            return;
        }

        syncedHeadPos.Value = rig.head.position;
        syncedHeadRot.Value = rig.head.rotation;

        syncedLeftHandPos.Value = rig.leftHand.position;
        syncedLeftHandRot.Value = rig.leftHand.rotation;

        syncedRightHandPos.Value = rig.rightHand.position;
        syncedRightHandRot.Value = rig.rightHand.rotation;

        syncedHipsPos.Value = rig.hips.position;
        syncedHipsRot.Value = rig.hips.rotation;

        for (int i = 0; i < expressionsToTrack.Length; i++)
        {
            float weight = rig.faceExpressions.GetWeight(expressionsToTrack[i]);
            if (i < syncedExpressionWeights.Count)
                syncedExpressionWeights[i] = weight;
        }

        syncedEyeForward.Value = rig.GetEyeForward();
    }

    private void ApplySyncData()
    {
        if (headTarget != null)
            headTarget.SetPositionAndRotation(syncedHeadPos.Value, syncedHeadRot.Value);

        if (leftHandTarget != null)
            leftHandTarget.SetPositionAndRotation(syncedLeftHandPos.Value, syncedLeftHandRot.Value);

        if (rightHandTarget != null)
            rightHandTarget.SetPositionAndRotation(syncedRightHandPos.Value, syncedRightHandRot.Value);

        if (pelvisTarget != null)
            pelvisTarget.SetPositionAndRotation(syncedHipsPos.Value, syncedHipsRot.Value);
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

    private void ApplyEyeTracking()
    {
        if (eyeLookTarget != null)
        {
            eyeLookTarget.forward = syncedEyeForward.Value;
        }
    }
}