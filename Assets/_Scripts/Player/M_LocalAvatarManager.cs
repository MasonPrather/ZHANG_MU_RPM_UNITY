using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using ReadyPlayerMe.Core;
using RootMotion.FinalIK;

/// <summary>
/// M_LocalAvatarManager
/// - Loads a local-only Ready Player Me avatar (full body, ARKit + Oculus Visemes).
/// - Parents it under localAvatarRoot.
/// - Sets up VRIK using OVR anchors (or fallback transforms).
/// - Hooks OVRFaceExpressions into M_LocalFaceDriver for local facial animation.
/// - Binds the local face SkinnedMeshRenderer to M_NetFaceMirror so the
///   local facial pose can be mirrored to all networked avatars.
/// - Automatically attaches M_FaceDebugProbe to the local avatar for diagnostics.
/// </summary>
public class M_LocalAvatarManager : MonoBehaviour
{
    /// <summary>
    /// Last spawned local face SMR, primarily for debugging or ad-hoc access.
    /// </summary>
    public static SkinnedMeshRenderer LastLocalFaceSMR { get; private set; }

    [Header("Mount")]
    [SerializeField] private Transform localAvatarRoot;   // Where the RPM avatar will be parented

    [Header("RPM")]
    [SerializeField]
    private string fallbackUrl =
        "https://models.readyplayer.me/67f49ac1b494e717079b6019.glb?morphTargets=ARKit,Oculus%20Visemes";

    [Header("First-Person View")]
    [SerializeField] private bool hideHeadMeshes = true;

    [Header("IK Targets (sources from your rig)")]
    [SerializeField] private Transform headSrc;
    [SerializeField] private Transform leftHandSrc;
    [SerializeField] private Transform rightHandSrc;
    [SerializeField] private bool autoFindOVRAnchors = true;

    [Header("Face (optional)")]
    [SerializeField] private OVRFaceExpressions faceExpressions;

    [Header("Face Debug Probe (optional)")]
    [SerializeField] private bool attachFaceDebugProbe = true;
    [SerializeField, Range(0.05f, 1.0f)] private float probeLogInterval = 0.25f;
    [SerializeField, Range(1, 64)] private int probeMaxLoggedBlendshapes = 24;

    private AvatarObjectLoader _loader;
    private GameObject _avatar;     // spawned RPM avatar (humanoid)
    private VRIK _vrik;             // VRIK placed on the RPM avatar

    private void Awake()
    {
        if (!localAvatarRoot)
            localAvatarRoot = transform;

        _loader = new AvatarObjectLoader();
        _loader.AvatarConfig = new AvatarConfig
        {
            MorphTargets = new List<string> { "ARKit", "Oculus Visemes" }
        };
        _loader.OnCompleted += OnCompleted;
        _loader.OnFailed += OnFailed;

        if (autoFindOVRAnchors)
            TryBindOVRAnchors();
    }

    private void Start()
    {
        // Use same URL you publish via network
        var url = PlayerPrefs.GetString("RPM_URL", fallbackUrl);
        if (string.IsNullOrWhiteSpace(url))
            url = fallbackUrl;

        Debug.Log($"[M_LocalAvatarManager] Loading local RPM: {url}");
        _loader.LoadAvatar(url);
    }

    private void OnDestroy()
    {
        if (_loader != null)
        {
            _loader.OnCompleted -= OnCompleted;
            _loader.OnFailed -= OnFailed;
        }
    }

    private void OnCompleted(object _, CompletionEventArgs e)
    {
        if (e?.Avatar == null)
        {
            Debug.LogError("[M_LocalAvatarManager] RPM completion returned null avatar.");
            return;
        }

        _avatar = e.Avatar;
        _avatar.transform.SetParent(localAvatarRoot, false);
        _avatar.transform.localPosition = Vector3.zero;
        _avatar.transform.localRotation = Quaternion.identity;
        _avatar.transform.localScale = Vector3.one;

        // Animator setup (ensure humanoid animator)
        var animator = _avatar.GetComponentInChildren<Animator>() ?? _avatar.AddComponent<Animator>();
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // Hide face/head meshes for 1st-person view
        if (hideHeadMeshes)
            HideHeadMesh(_avatar);

        // Re-bind OVR anchors if requested and still missing
        if (autoFindOVRAnchors && (!headSrc || !leftHandSrc || !rightHandSrc))
            TryBindOVRAnchors();

        // --- VRIK setup ---
        _vrik = _avatar.GetComponent<VRIK>();
        if (_vrik == null)
            _vrik = _avatar.AddComponent<VRIK>();

        _vrik.AutoDetectReferences();

        if (headSrc) _vrik.solver.spine.headTarget = headSrc;
        if (leftHandSrc) _vrik.solver.leftArm.target = leftHandSrc;
        if (rightHandSrc) _vrik.solver.rightArm.target = rightHandSrc;

        _vrik.solver.plantFeet = false;
        _vrik.solver.spine.minHeadHeight = 0f;
        _vrik.solver.IKPositionWeight = 1f;

        // --- Face tracking setup ---
        if (faceExpressions == null)
            faceExpressions = FindObjectOfType<OVRFaceExpressions>();

        if (faceExpressions != null)
        {
            faceExpressions.enabled = true;
            Debug.Log("[M_LocalAvatarManager] Face Tracking Enabled: " + faceExpressions.FaceTrackingEnabled);
        }

        // Find the face SkinnedMeshRenderer on the local RPM avatar
        var faceSmr = FindFaceMesh(_avatar);

        // Hook up local face driver (OVRFaceExpressions -> blendshapes on local RPM avatar)
        if (faceSmr != null && faceExpressions != null)
        {
            var faceDriver = _avatar.AddComponent<M_LocalFaceDriver>();
            faceDriver.Initialize(faceExpressions, faceSmr);
            Debug.Log("[M_LocalAvatarManager] M_LocalFaceDriver initialized on local RPM avatar.");

            LastLocalFaceSMR = faceSmr;

            // --- Auto-attach M_FaceDebugProbe for diagnostics ---
            if (attachFaceDebugProbe)
            {
                var probe = _avatar.AddComponent<M_FaceDebugProbe>();
                probe.Initialize(
                    faceExpressions,
                    faceSmr,
                    true
                );

                Debug.Log("[M_LocalAvatarManager] M_FaceDebugProbe attached and initialized on local RPM avatar.");
            }
        }
        else
        {
            Debug.LogWarning("[M_LocalAvatarManager] Could not initialize face driver (faceSmr or faceExpressions missing). " +
                             "Face debug probe will NOT be attached.");
        }

        // (You currently have NetFaceMirror binding commented out; leaving it that way for now.)

        Debug.Log("[M_LocalAvatarManager] Local RPM ready (VRIK bound to OVR anchors).");
    }

    /// <summary>
    /// Finds the primary face/head SkinnedMeshRenderer on the given avatar.
    /// </summary>
    private SkinnedMeshRenderer FindFaceMesh(GameObject avatarGO)
    {
        // Prefer the visible head mesh; even if you disable it for 1st-person,
        // blendshape weights still apply and can be read for networking.
        foreach (var smr in avatarGO.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;

            if (smr.name.IndexOf("Renderer_Head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                smr.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return smr;
            }
        }

        return null;
    }

    private void OnFailed(object _, FailureEventArgs e)
    {
        Debug.LogError($"[M_LocalAvatarManager] RPM load failed: {e?.Type} {e?.Message}");
    }

    /// <summary>
    /// Disables face/eye/head meshes by name for first-person view.
    /// </summary>
    private void HideHeadMesh(GameObject avatarGO)
    {
        string[] hideNames =
        {
            "Renderer_EyeLeft",
            "Renderer_EyeRight",
            "Renderer_Head",
            "Renderer_Teeth",
            "Renderer_Hair"
        };

        int hidden = 0;
        foreach (var smr in avatarGO.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;

            string nameLower = smr.name.ToLowerInvariant();
            foreach (var target in hideNames)
            {
                if (nameLower.Contains(target.ToLowerInvariant()))
                {
                    smr.enabled = false;
                    hidden++;
                    break;
                }
            }
        }

        Debug.Log($"[M_LocalAvatarManager] Hidden {hidden} face/eye/head meshes by name.");
    }

    /// <summary>
    /// Attempts to automatically bind OVR anchors from OVRCameraRig.
    /// Falls back to Main Camera / XR origin-style names if needed.
    /// </summary>
    private void TryBindOVRAnchors()
    {
        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig)
        {
            if (!headSrc) headSrc = rig.centerEyeAnchor;
            if (!leftHandSrc) leftHandSrc = rig.leftControllerAnchor;
            if (!rightHandSrc) rightHandSrc = rig.rightControllerAnchor;

            if (headSrc && leftHandSrc && rightHandSrc)
                Debug.Log("[M_LocalAvatarManager] Auto-bound OVR anchors (centerEye, L/R controller).");
        }
        else
        {
            // Fallback by names if someone uses XR Origin naming
            if (!headSrc)
            {
                var cam = Camera.main ? Camera.main.transform : GameObject.Find("Main Camera")?.transform;
                if (cam) headSrc = cam;
            }

            if (!leftHandSrc)
                leftHandSrc = GameObject.Find("LeftHand Controller")?.transform;

            if (!rightHandSrc)
                rightHandSrc = GameObject.Find("RightHand Controller")?.transform;
        }
    }
}