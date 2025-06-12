using UnityEngine;
using ReadyPlayerMe.Core;
using Meta.XR.Movement;

public class M_Final_RigReferences : MonoBehaviour
{
    public static M_Final_RigReferences Singleton { get; private set; }

    [Header("Tracked XR Device Targets")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public Transform hips;

    [Header("Avatar Expression References")]
    public OVRFaceExpressions faceExpressions;
    public OVREyeGaze eyeGaze;

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
        faceExpressions = avatar.GetComponentInChildren<OVRFaceExpressions>();
        eyeGaze = avatar.GetComponentInChildren<OVREyeGaze>();
    }

    public bool IsFaceTrackingValid()
    {
        return faceExpressions != null && faceExpressions.ValidExpressions;
    }

    public bool IsEyeTrackingValid()
    {
        return eyeGaze != null && eyeGaze.EyeTrackingEnabled;
    }

    public float GetBlendWeight(string expressionName)
    {
        if (faceExpressions == null) return 0f;

        if (System.Enum.TryParse(expressionName, out OVRFaceExpressions.FaceExpression exp))
        {
            return faceExpressions[exp];
        }

        return 0f;
    }

    public Vector3 GetEyeForward()
    {
        return IsEyeTrackingValid() ? eyeGaze.transform.forward : Vector3.forward;
    }
}