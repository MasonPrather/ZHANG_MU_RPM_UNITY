using UnityEngine;
using Oculus.Movement.AnimationRigging;
using Oculus.Movement.Utils;
using UnityEngine.Events;
using UnityEngine.Animations;
using ReadyPlayerMe.Core;

public class M_OVR_RigReferences : MonoBehaviour
{
    public static M_OVR_RigReferences Singleton { get; private set; }

    [Header("Avatar Bone References")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public Transform hips;

    [Header("Avatar Expression References")]
    public OVREyeGaze eyeGaze;
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

    /// <summary>
    /// Assigns references to head, hands, hips, eye gaze, and facial expressions from the loaded avatar.
    /// </summary>
    /// <param name="avatar">The GameObject containing the RPM avatar.</param>
    public void AssignAvatarReferences(GameObject avatar)
    {
        Animator animator = avatar.GetComponent<Animator>();
        if (animator != null)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        eyeGaze = avatar.GetComponentInChildren<OVREyeGaze>();
        faceExpressions = avatar.GetComponentInChildren<OVRFaceExpressions>();
    }
}
