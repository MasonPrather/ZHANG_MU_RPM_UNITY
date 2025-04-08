using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class M_VRRigReferences : MonoBehaviour
{
    public static M_VRRigReferences Singleton;

    public Transform root;
    public Transform head;
    public Transform hips;
    public Transform leftHand;
    public Transform rightHand;

    private void Awake()
    {
        Singleton = this;
    }
}