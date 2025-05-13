using UnityEngine;
using Oculus.Movement.Utils;
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
    /*
    public Transform leftFoot;
    public Transform rightFoot;
    public Transform spine;
    public Transform spine1;
    public Transform spine2;
    */
    [Header("Avatar Expression References")]
    //public OVREyeGaze eyeGaze;
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
        Animator animator = avatar.GetComponent<Animator>();
        if (animator != null)
        {
            //head = animator.GetBoneTransform(HumanBodyBones.Head);
            //leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            //rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            //hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            /*
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);            
            spine1 = animator.GetBoneTransform(HumanBodyBones.Chest);
            spine2 = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            */
        }

        //eyeGaze = avatar.GetComponentInChildren<OVREyeGaze>();
        faceExpressions = avatar.GetComponentInChildren<OVRFaceExpressions>();
    }
}