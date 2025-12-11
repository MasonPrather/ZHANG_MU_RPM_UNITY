using System.Collections;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;
using ReadyPlayerMe.Core;
using RootMotion.FinalIK;
using System;

/// <summary>
/// M_NetAvatar
/// 
/// REMOTE/NETWORK visual (also exists for the owner, but you can hide it):
/// - Loads RPM avatar under avatarRoot
/// - Adds VRIK on the avatar and binds to IKTargets (driven by M_NetPoseDriver)
/// - Waits for first remote pose (via M_NetPoseDriver) before enabling VRIK
/// - Finds the avatar's face SkinnedMeshRenderer and binds it to M_NetFaceMirror
/// - Sets layer per ownership (LocalAvatar / RemoteAvatar)
/// </summary>
[DisallowMultipleComponent]
public class M_NetAvatar : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private M_NetPlayer netPlayer;
    [SerializeField] private M_NetPoseDriver poseDriver;
    [SerializeField] private Transform avatarRoot;

    [Header("IK Targets (driven by NetPoseDriver on this prefab)")]
    [SerializeField] private Transform headTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;

    [Header("Face Networking")]
    [Tooltip("Face mirror component on this same player prefab (NetworkedPlayer root).")]
    [SerializeField] private M_NetFaceMirror faceMirror;

    [Header("Layers")]
    [SerializeField] private string ownerAvatarLayer = "LocalAvatar";
    [SerializeField] private string remoteAvatarLayer = "RemoteAvatar";

    private AvatarObjectLoader _loader;
    private GameObject _avatarGO;
    private VRIK _vrik;

    private void Awake()
    {
        if (!netPlayer) netPlayer = GetComponent<M_NetPlayer>();
        if (!poseDriver) poseDriver = GetComponent<M_NetPoseDriver>();
        if (!faceMirror) faceMirror = GetComponent<M_NetFaceMirror>();

        _loader = new AvatarObjectLoader();
        _loader.OnCompleted += OnAvatarCompleted;
        _loader.OnFailed += OnAvatarFailed;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!netPlayer)
        {
            Debug.LogError("[M_NetAvatar] Missing M_NetPlayer.");
            return;
        }

        // Listen for URL changes coming from lobby / player data
        netPlayer.AvatarUrl.OnValueChanged += OnUrlChanged;

        // Load immediately if already set
        if (netPlayer.AvatarUrl.Value.Length > 0)
        {
            Load(netPlayer.AvatarUrl.Value.ToString());
        }

        // Ownership-based visual tweaks can be handled here if needed
    }

    private void OnDestroy()
    {
        if (_loader != null)
        {
            _loader.OnCompleted -= OnAvatarCompleted;
            _loader.OnFailed -= OnAvatarFailed;
        }

        if (netPlayer != null)
        {
            netPlayer.AvatarUrl.OnValueChanged -= OnUrlChanged;
        }
    }

    private void OnUrlChanged(FixedString512Bytes prev, FixedString512Bytes next)
    {
        if (next.Length > 0)
            Load(next.ToString());
    }

    private void Load(string url)
    {
        if (_avatarGO)
            Destroy(_avatarGO);

        _loader.LoadAvatar(url);
    }

    private void OnAvatarCompleted(object sender, CompletionEventArgs e)
    {
        if (!e?.Avatar)
        {
            Debug.LogError("[M_NetAvatar] RPM completion returned null avatar.");
            return;
        }

        _avatarGO = e.Avatar;

        _avatarGO.transform.SetParent(avatarRoot, false);
        _avatarGO.transform.localPosition = Vector3.zero;
        _avatarGO.transform.localRotation = Quaternion.identity;
        _avatarGO.transform.localScale = Vector3.one;

        // --- Animator setup ---
        var anim = _avatarGO.GetComponentInChildren<Animator>(true) ?? _avatarGO.AddComponent<Animator>();
        anim.updateMode = AnimatorUpdateMode.Normal;            // Not AnimatePhysics on Quest
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;    // Evaluate even if culled
        anim.applyRootMotion = false;                                // Net pose drives movement

        anim.Rebind();
        anim.Update(0f);

        // Keep SMRs updating right after spawn (avoids 1-frame pop-in on Quest)
        KeepSMRsUpdating(_avatarGO, true);

        // --- VRIK wiring ---
        _vrik = _avatarGO.GetComponent<VRIK>() ?? _avatarGO.AddComponent<VRIK>();
        _vrik.AutoDetectReferences();

        if (headTarget) _vrik.solver.spine.headTarget = headTarget;
        if (leftHandTarget) _vrik.solver.leftArm.target = leftHandTarget;
        if (rightHandTarget) _vrik.solver.rightArm.target = rightHandTarget;

        _vrik.solver.plantFeet = false;
        _vrik.solver.spine.minHeadHeight = 0f;

        // Gate VRIK until first remote pose arrives to avoid "burrito collapse"
        if (!poseDriver)
        {
            _vrik.enabled = true;
        }
        else
        {
            _vrik.enabled = false;
            StartCoroutine(EnableVrikWhenReady());
        }

        // --- Bind avatar face mesh to M_NetFaceMirror (for networked facial mirroring) ---
        var faceSmr = FindFaceSMR(_avatarGO);
        if (faceSmr != null)
        {
            if (!faceMirror)
            {
                faceMirror = GetComponent<M_NetFaceMirror>() ??
                             GetComponentInChildren<M_NetFaceMirror>(true);
            }

            if (faceMirror != null)
            {
                faceMirror.BindAvatarFace(faceSmr);
                Debug.Log("[M_NetAvatar] Bound face SMR to M_NetFaceMirror.");

            }
            else
            {
                Debug.LogWarning("[M_NetAvatar] No M_NetFaceMirror found on this player prefab; cannot bind face SMR.");
            }
        }
        else
        {
            Debug.LogWarning("[M_NetAvatar] Could not find a face SMR to bind (Renderer_Head not found).");
        }

        // Ownership-based visible layer
        int layer = LayerMask.NameToLayer(IsOwner ? ownerAvatarLayer : remoteAvatarLayer);
        if (layer != -1)
            SetLayerRecursive(_avatarGO, layer);

        Debug.Log("[M_NetAvatar] RPM avatar loaded; Animator primed; VRIK wired to IKTargets.");
    }

    private IEnumerator EnableVrikWhenReady()
    {
        while (poseDriver && !poseDriver.HasRemotePose)
            yield return null;

        _vrik.enabled = true;
    }

    private void OnAvatarFailed(object sender, FailureEventArgs args)
    {
        Debug.LogError($"[M_NetAvatar] RPM load failed [{args.Type}] {args.Message}");
    }

    private static void KeepSMRsUpdating(GameObject root, bool enable)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = enable;
        }
    }

    /// <summary>
    /// Finds the primary face/head SkinnedMeshRenderer on the given avatar.
    /// </summary>
    private SkinnedMeshRenderer FindFaceSMR(GameObject avatar)
    {
        if (!avatar) return null;

        // Try direct child named "Renderer_Head" (RPM default)
        var head = avatar.transform.Find("Renderer_Head");
        if (head)
        {
            var smr = head.GetComponent<SkinnedMeshRenderer>();
            if (smr)
            {
                Debug.Log("[M_NetAvatar] Found face mesh: Renderer_Head");
                return smr;
            }
        }

        // Fallback: case-insensitive search
        var all = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in all)
        {
            if (!smr) continue;

            var n = smr.name.ToLowerInvariant();
            if (smr.name.Equals("Renderer_Head", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("renderer_head") ||
                n.Contains("head"))
            {
                Debug.Log($"[M_NetAvatar] Found face mesh by search: {smr.name}");
                return smr;
            }
        }

        Debug.LogWarning("[M_NetAvatar] Could not find a face SMR (Renderer_Head).");
        return null;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        if (!go) return;

        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }
    }
}