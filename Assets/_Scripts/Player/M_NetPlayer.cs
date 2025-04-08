using Unity.Netcode;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    public Transform root;
    public Transform head;
    public Transform hips;
    public Transform leftHand;
    public Transform rightHand;

    public Renderer[] meshToDisable;

    private void Start()
    {
        if(IsOwner)
        {
            foreach (var item in meshToDisable)
            {
                item.enabled = false;
            }
        }
        
    }

    private void Update()
    {
        if(IsOwner)
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
        
    }
}