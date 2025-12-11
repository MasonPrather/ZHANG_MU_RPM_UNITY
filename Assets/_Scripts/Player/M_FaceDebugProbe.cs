using System;
using UnityEngine;

/// <summary>
/// M_FaceDebugProbe
/// 
/// Purpose:
/// - Sample the local RPM face mesh over a short "neutral" window,
///   and compute per-blendshape min / max / avg weights.
/// - Log the summary to Logcat so we can reason about neutral baselines
///   and which morph targets are doing cursed stretching.
/// 
/// Usage:
/// - M_LocalAvatarManager calls Initialize(faceExpressions, localFaceMesh, true).
/// - For the first `neutralSampleDuration` seconds, this will assume you are
///   holding a neutral expression and will gather stats.
/// - At the end, it prints a summary for each blendshape:
///   IDX, Name, Min, Max, Avg.
/// 
/// Notes:
/// - We sample the **local RPM SkinnedMeshRenderer** (post-mapping),
///   which is what your local and networked avatars actually use.
/// </summary>
public class M_FaceDebugProbe : MonoBehaviour
{
    [Header("Bindings (auto-filled by M_LocalAvatarManager)")]
    [SerializeField] private OVRFaceExpressions faceExpressions;
    [SerializeField] private SkinnedMeshRenderer localFaceMesh;

    [Header("Neutral Sampling")]
    [Tooltip("If true, automatically sample a neutral expression window on Start.")]
    [SerializeField] private bool autoNeutralSampleOnStart = true;

    [Tooltip("Duration (seconds) to sample neutral expression.")]
    [SerializeField] private float neutralSampleDuration = 4.0f;

    [Tooltip("Weights below this are considered 'effectively zero' and may be omitted from logs.")]
    [SerializeField] private float neutralLogEpsilon = 0.01f;

    [Header("Optional Per-Frame Debugging")]
    [Tooltip("If true, logs a small subset of blendshapes every interval for live debugging.")]
    [SerializeField] private bool livePerFrameLog = false;

    [Tooltip("Interval in seconds between live logs when enabled.")]
    [SerializeField] private float liveLogInterval = 0.5f;

    // --- Internal state for neutral sampling ---
    private bool _samplingNeutral = false;
    private float _neutralTimer = 0f;
    private int _neutralSampleCount = 0;

    private float[] _sum;
    private float[] _min;
    private float[] _max;

    // --- Internal state for live logging ---
    private float _liveTimer = 0f;

    #region Public API

    /// <summary>
    /// Called by M_LocalAvatarManager once face tracking + local face mesh exist.
    /// </summary>
    public void Initialize(OVRFaceExpressions expressions, SkinnedMeshRenderer faceMesh, bool startNeutralSample)
    {
        faceExpressions = expressions;
        localFaceMesh = faceMesh;

        if (localFaceMesh == null)
        {
            Debug.LogWarning("[M_FaceDebugProbe] Initialize called with null localFaceMesh.");
            return;
        }

        Debug.Log("[M_FaceDebugProbe] Initialize - localFaceMesh=" + localFaceMesh.name +
                  ", autoStart=" + startNeutralSample);

        if (startNeutralSample)
        {
            StartNeutralSampling();
        }
    }

    /// <summary>
    /// Manually start a new neutral sampling window.
    /// </summary>
    public void StartNeutralSampling()
    {
        if (localFaceMesh == null || localFaceMesh.sharedMesh == null)
        {
            Debug.LogWarning("[M_FaceDebugProbe] StartNeutralSampling failed: localFaceMesh missing or has no sharedMesh.");
            return;
        }

        int count = localFaceMesh.sharedMesh.blendShapeCount;
        if (count == 0)
        {
            Debug.LogWarning("[M_FaceDebugProbe] StartNeutralSampling: localFaceMesh has 0 blendshapes.");
            return;
        }

        _sum = new float[count];
        _min = new float[count];
        _max = new float[count];

        for (int i = 0; i < count; i++)
        {
            _sum[i] = 0f;
            _min[i] = float.PositiveInfinity;
            _max[i] = float.NegativeInfinity;
        }

        _neutralSampleCount = 0;
        _neutralTimer = 0f;
        _samplingNeutral = true;

        Debug.Log($"[M_FaceDebugProbe] Neutral sampling STARTED for {neutralSampleDuration:F2} sec. " +
                  "Hold a neutral face (relaxed, eyes open, mouth closed).");
    }

    /// <summary>
    /// Stops sampling and dumps the summary (if any samples were taken).
    /// You can call this manually if you want to end early.
    /// </summary>
    public void StopNeutralSamplingAndDump()
    {
        if (!_samplingNeutral)
            return;

        _samplingNeutral = false;
        DumpNeutralSummary();
    }

    #endregion

    #region Unity

    private void Start()
    {
        // If something forgot to call Initialize, we still try to auto-bind,
        // but init is best done explicitly from M_LocalAvatarManager.
        if (localFaceMesh == null)
        {
            TryAutoFindLocalFaceMesh();
        }

        if (autoNeutralSampleOnStart && localFaceMesh != null)
        {
            StartNeutralSampling();
        }
    }

    private void Update()
    {
        // Live light logging for quick sanity checks (optional).
        if (livePerFrameLog && localFaceMesh != null && localFaceMesh.sharedMesh != null)
        {
            _liveTimer += Time.deltaTime;
            if (_liveTimer >= liveLogInterval)
            {
                _liveTimer = 0f;
                LogLiveSubset();
            }
        }

        // Neutral sampling window.
        if (_samplingNeutral && localFaceMesh != null && localFaceMesh.sharedMesh != null)
        {
            _neutralTimer += Time.deltaTime;
            AccumulateNeutralSample();

            if (_neutralTimer >= neutralSampleDuration)
            {
                StopNeutralSamplingAndDump();
            }
        }
    }

    #endregion

    #region Internal - Neutral Sampling

    private void AccumulateNeutralSample()
    {
        var mesh = localFaceMesh.sharedMesh;
        int count = mesh.blendShapeCount;
        if (count == 0) return;

        _neutralSampleCount++;

        for (int i = 0; i < count; i++)
        {
            float w = localFaceMesh.GetBlendShapeWeight(i);

            _sum[i] += w;

            if (w < _min[i]) _min[i] = w;
            if (w > _max[i]) _max[i] = w;
        }
    }

    private void DumpNeutralSummary()
    {
        if (_neutralSampleCount <= 0 || _sum == null || _min == null || _max == null)
        {
            Debug.LogWarning("[M_FaceDebugProbe] Neutral summary: no samples recorded.");
            return;
        }

        var mesh = localFaceMesh.sharedMesh;
        int count = mesh.blendShapeCount;

        Debug.Log("===== [M_FaceDebugProbe] Neutral Sample SUMMARY =====");
        Debug.Log($"Samples:     N = {_neutralSampleCount}");
        Debug.Log($"Mesh:        {localFaceMesh.name}");
        Debug.Log($"BlendShapes: {count}");
        Debug.Log($"Epsilon:     {neutralLogEpsilon:F4}");
        Debug.Log("-----------------------------------------------------");

        for (int i = 0; i < count; i++)
        {
            float avg = _sum[i] / _neutralSampleCount;
            float min = _min[i];
            float max = _max[i];

            // Optionally skip ultra-tiny noise.
            if (Mathf.Abs(avg) < neutralLogEpsilon &&
                Mathf.Abs(min) < neutralLogEpsilon &&
                Mathf.Abs(max) < neutralLogEpsilon)
            {
                continue;
            }

            string name = mesh.GetBlendShapeName(i);
            Debug.Log(
                $"IDX={i:000} | name={name} | min={min:F4} | max={max:F4} | avg={avg:F4}");
        }

        Debug.Log("===== [M_FaceDebugProbe] Neutral Sample END =====");
    }

    #endregion

    #region Internal - Live Subset Log

    /// <summary>
    /// Lightweight periodic log for quick in-headset sanity checks.
    /// </summary>
    private void LogLiveSubset()
    {
        var mesh = localFaceMesh.sharedMesh;
        int count = mesh.blendShapeCount;
        if (count == 0) return;

        // Just log the first few blendshapes (or fewer if the mesh is tiny).
        int subset = Mathf.Min(8, count);

        string line = "[M_FaceDebugProbe] Live subset: ";
        for (int i = 0; i < subset; i++)
        {
            float w = localFaceMesh.GetBlendShapeWeight(i);
            string name = mesh.GetBlendShapeName(i);
            line += $"[{i}:{name}={w:F2}] ";
        }

        Debug.Log(line);
    }

    #endregion

    #region Internal - Auto Find

    private void TryAutoFindLocalFaceMesh()
    {
        // Try to find something that looks like a head mesh on this avatar.
        var smrList = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrList)
        {
            if (smr == null || smr.sharedMesh == null) continue;

            string n = smr.name.ToLowerInvariant();
            if (n.Contains("renderer_head") || n.Contains("head"))
            {
                localFaceMesh = smr;
                Debug.Log("[M_FaceDebugProbe] Auto-bound localFaceMesh=" + smr.name);
                return;
            }
        }

        Debug.LogWarning("[M_FaceDebugProbe] Auto-find failed: no head/face mesh detected.");
    }

    #endregion
}