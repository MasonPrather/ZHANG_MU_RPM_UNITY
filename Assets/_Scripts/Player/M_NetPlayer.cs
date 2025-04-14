using Unity.Netcode;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    [Header("Body References")]
    public Transform root;
    public Transform head;
    public Transform hips;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    private Vector3 lastPosition;

    private NetworkVariable<float> syncedMoveSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> syncedHmdRotation = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Mesh to Hide on Local Player")]
    public Renderer[] meshToDisable;

    private void Start()
    {
        if (IsOwner)
        {
            foreach (var item in meshToDisable)
            {
                item.enabled = false;
            }

            lastPosition = root.position;
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            UpdatePose();
            UpdateAnimationParameters();
        }

        ApplyAnimationParameters();
    }

    private void UpdatePose()
    {
        root.position = M_VRRigReferences.Singleton.root.position;
        root.rotation = M_VRRigReferences.Singleton.root.rotation;

        head.position = M_VRRigReferences.Singleton.head.position;
        head.rotation = M_VRRigReferences.Singleton.head.rotation;

        hips.position = M_VRRigReferences.Singleton.hips.position;
        hips.rotation = M_VRRigReferences.Singleton.hips.rotation;

        leftHand.position = M_VRRigReferences.Singleton.leftHand.position;
        leftHand.rotation = M_VRRigReferences.Singleton.leftHand.rotation;

        rightHand.position = M_VRRigReferences.Singleton.rightHand.position;
        rightHand.rotation = M_VRRigReferences.Singleton.rightHand.rotation;
    }

    private void UpdateAnimationParameters()
    {
        Vector3 horizontalDelta = root.position - lastPosition;
        horizontalDelta.y = 0f;
        float speed = horizontalDelta.magnitude / Time.deltaTime;
        lastPosition = root.position;

        float turn = 0f;
        Vector3 velocityDir = horizontalDelta.normalized;
        if (velocityDir.sqrMagnitude > 0.01f)
        {
            Vector3 forward = head.forward;
            turn = Vector3.SignedAngle(forward, velocityDir, Vector3.up) / 90f;
        }

        syncedMoveSpeed.Value = Mathf.Clamp(speed, 0f, 2f);
        syncedHmdRotation.Value = Mathf.Clamp(turn, -1f, 1f);
    }

    private void ApplyAnimationParameters()
    {
        if (animator == null) return;

        float smoothedSpeed = Mathf.Lerp(animator.GetFloat("moveSpeed"), syncedMoveSpeed.Value, Time.deltaTime * 10f);
        float smoothedTurn = Mathf.Lerp(animator.GetFloat("hmdRotation"), syncedHmdRotation.Value, Time.deltaTime * 10f);

        animator.SetFloat("moveSpeed", smoothedSpeed);
        animator.SetFloat("hmdRotation", smoothedTurn);
    }
}