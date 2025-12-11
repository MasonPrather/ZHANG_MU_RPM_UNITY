using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M_LocalFaceDriver
/// 
/// - Reads OVRFaceExpressions every frame.
/// - Maps as many OVR expressions as possible onto ARKit-style RPM blendshapes.
/// - Applies smoothing + gain locally (0–100 range).
/// - Aggressively relaxes ALL blendshapes back to 0 when expressions are neutral
///   or when tracking is invalid.
/// 
/// This is the ONLY component that writes to the local RPM face mesh.
/// M_NetFaceMirror simply samples these weights and mirrors them over the network.
/// </summary>
public class M_LocalFaceDriver : MonoBehaviour
{
    private const float HARD_MAX_WEIGHT = 100f; // absolute safety clamp

    [Header("Sources")]
    [SerializeField] private OVRFaceExpressions faceSource;
    [SerializeField] private SkinnedMeshRenderer faceMesh;

    [Header("Intensity / Smoothing")]
    [Tooltip("Global gain applied to all driven channels.")]
    [Range(0.1f, 3.0f)] public float globalGain = 1.0f;

    [Tooltip("0 = no smoothing, 1 = very slow.")]
    [Range(0.0f, 1.0f)] public float smoothing = 0.4f;

    [Tooltip("Maximum local weight (before M_NetFaceMirror scaling).")]
    [Range(10f, 100f)] public float maxWeight = 80f;

    [Header("Neutral Handling")]
    [Tooltip("If total expression magnitude is below this, treat as neutral.")]
    [Range(0.0f, 0.3f)] public float globalNeutralThreshold = 0.05f;

    [Tooltip("How fast we relax to 0 when neutral (1 = instant).")]
    [Range(0.0f, 1.0f)] public float neutralRelaxFactor = 0.7f;

    [Tooltip("Per-channel cutoff: if a source expr is below this, its target is 0.")]
    [Range(0.0f, 0.2f)] public float perChannelMin = 0.02f;

    // Internal state
    private float[] _currentWeights;   // one per blendshape index
    private bool _initialized;
    private Mesh _mesh;

    private readonly List<Channel> _channels = new List<Channel>();

    [Serializable]
    private struct Channel
    {
        public OVRFaceExpressions.FaceExpression expression; // OVR source
        public int blendIndex;                               // RPM/ARKit target index
        public float gain;                                   // per-channel gain multiplier
    }

    #region Public API

    /// <summary>
    /// Called by M_LocalAvatarManager after the RPM avatar is loaded.
    /// </summary>
    public void Initialize(OVRFaceExpressions source, SkinnedMeshRenderer smr)
    {
        faceSource = source;
        faceMesh = smr;

        if (faceMesh == null || faceMesh.sharedMesh == null)
        {
            Debug.LogError("[M_LocalFaceDriver] Initialize failed: faceMesh or sharedMesh is null.");
            return;
        }

        _mesh = faceMesh.sharedMesh;
        int count = _mesh.blendShapeCount;
        if (count <= 0)
        {
            Debug.LogWarning("[M_LocalFaceDriver] Face mesh has no blendshapes.");
            return;
        }

        _currentWeights = new float[count];
        for (int i = 0; i < count; i++)
            _currentWeights[i] = 0f;

        BuildChannelMap();

        _initialized = true;

        Debug.Log($"[M_LocalFaceDriver] Initialized with {count} blendshapes and {_channels.Count} mapped channels.");
    }

    #endregion

    #region Unity

    private void Update()
    {
        if (!_initialized || faceMesh == null || _mesh == null)
            return;

        int count = _mesh.blendShapeCount;
        if (_currentWeights == null || _currentWeights.Length != count)
        {
            _currentWeights = new float[count];
            for (int i = 0; i < count; i++)
                _currentWeights[i] = 0f;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 1f / 60f;

        // If tracking data is invalid, just relax everything toward 0.
        if (faceSource == null || !faceSource.ValidExpressions || _channels.Count == 0)
        {
            RelaxAllToNeutral(dt);
            ApplyWeightsToMesh();
            return;
        }

        // 1) Compute a rough total expression magnitude for "are we neutral?"
        float totalMagnitude = 0f;
        foreach (var ch in _channels)
        {
            float w = faceSource.GetWeight(ch.expression); // 0..1
            totalMagnitude += Mathf.Abs(w);
        }

        // 2) If basically neutral → aggressively relax to 0 across the board.
        if (totalMagnitude < globalNeutralThreshold)
        {
            RelaxAllToNeutral(dt);
            ApplyWeightsToMesh();
            return;
        }

        // 3) Not neutral → drive channels.
        DriveChannels(dt);
        ApplyWeightsToMesh();
    }

    #endregion

    #region Driving Logic

    private void DriveChannels(float dt)
    {
        if (_channels.Count == 0)
            return;

        int count = _currentWeights.Length;

        // Start with all targets at 0 so unused indices also relax.
        float[] targetWeights = new float[count];
        for (int i = 0; i < count; i++)
            targetWeights[i] = 0f;

        // For each mapped channel, compute its target based on the OVR expression.
        foreach (var ch in _channels)
        {
            float src = faceSource.GetWeight(ch.expression); // 0..1

            // Small noise → snap to 0.
            if (src < perChannelMin)
                continue;

            float t = src * 100f * ch.gain * globalGain; // convert to 0–100-ish

            // Soft clamp to user max, then hard clamp to Unity-safe 0–100
            t = Mathf.Clamp(t, 0f, maxWeight);
            t = Mathf.Clamp(t, 0f, HARD_MAX_WEIGHT);

            int idx = ch.blendIndex;
            if (idx < 0 || idx >= count)
                continue;

            // If multiple channels map to same index, keep the stronger one.
            if (t > targetWeights[idx])
                targetWeights[idx] = t;
        }

        // Smoothly move current weights toward the target weights.
        float factor = smoothing <= 0f
            ? 1f
            : 1f - Mathf.Pow(1f - smoothing, dt * 60f);

        for (int i = 0; i < count; i++)
        {
            float current = _currentWeights[i];
            float target = targetWeights[i];

            float blended = Mathf.Lerp(current, target, factor);

            // Clamp after smoothing as well, just in case.
            _currentWeights[i] = Mathf.Clamp(blended, 0f, Mathf.Min(maxWeight, HARD_MAX_WEIGHT));
        }
    }

    private void RelaxAllToNeutral(float dt)
    {
        if (_currentWeights == null)
            return;

        float factor = neutralRelaxFactor <= 0f
            ? 1f
            : 1f - Mathf.Pow(1f - neutralRelaxFactor, dt * 60f);

        for (int i = 0; i < _currentWeights.Length; i++)
        {
            float blended = Mathf.Lerp(_currentWeights[i], 0f, factor);
            _currentWeights[i] = Mathf.Clamp(blended, 0f, Mathf.Min(maxWeight, HARD_MAX_WEIGHT));
        }
    }

    private void ApplyWeightsToMesh()
    {
        if (faceMesh == null || _mesh == null || _currentWeights == null)
            return;

        int count = Mathf.Min(_mesh.blendShapeCount, _currentWeights.Length);
        float maxClamp = Mathf.Min(maxWeight, HARD_MAX_WEIGHT);

        for (int i = 0; i < count; i++)
        {
            // Final safety clamp before pushing to the renderer
            float w = Mathf.Clamp(_currentWeights[i], 0f, maxClamp);
            _currentWeights[i] = w; // keep internal state consistent

            faceMesh.SetBlendShapeWeight(i, w);
        }
    }

    #endregion

    #region Channel Mapping

    private int FindBlendIndex(params string[] nameFragments)
    {
        if (_mesh == null) return -1;

        int count = _mesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            string name = _mesh.GetBlendShapeName(i);
            string lower = name.ToLowerInvariant();

            bool allMatch = true;
            foreach (var frag in nameFragments)
            {
                if (!lower.Contains(frag.ToLowerInvariant()))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
                return i;
        }
        return -1;
    }

    private void AddChannelIfFound(
        OVRFaceExpressions.FaceExpression expr,
        float gain,
        params string[] fragments)
    {
        int idx = FindBlendIndex(fragments);
        if (idx < 0) return;

        _channels.Add(new Channel
        {
            expression = expr,
            blendIndex = idx,
            gain = gain
        });
    }

    /// <summary>
    /// Attempts to auto-map as many OVR expressions as possible
    /// to ARKit-style RPM blendshapes by name.
    /// </summary>
    private void BuildChannelMap()
    {
        _channels.Clear();
        if (_mesh == null) return;

        // --------------------
        // JAW / MOUTH OPEN / SIDEWAYS / FORWARD
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.JawDrop,
            1.0f,
            "jaw", "open");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.JawDrop,
            1.0f,
            "mouth", "open"); // fallback

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.JawThrust,
            0.8f,
            "jaw", "forward");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.JawSidewaysLeft,
            0.8f,
            "jaw", "left");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.JawSidewaysRight,
            0.8f,
            "jaw", "right");

        // --------------------
        // MOUTH CLOSE / PRESS
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipPressorL,
            0.8f,
            "mouthpress_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipPressorR,
            0.8f,
            "mouthpress_r");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipsToward,
            0.6f,
            "mouthclose");

        // --------------------
        // SMILE / FROWN / DIMPLES / STRETCH
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerPullerL,
            1.2f,
            "mouth", "smile", "l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerPullerL,
            1.2f,
            "mouthsmile_l");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerPullerR,
            1.2f,
            "mouth", "smile", "r");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerPullerR,
            1.2f,
            "mouthsmile_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerDepressorL,
            1.0f,
            "mouthfrown_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipCornerDepressorR,
            1.0f,
            "mouthfrown_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.DimplerL,
            0.9f,
            "mouthdimple_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.DimplerR,
            0.9f,
            "mouthdimple_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipStretcherL,
            0.9f,
            "mouthstretch_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipStretcherR,
            0.9f,
            "mouthstretch_r");

        // --------------------
        // MOUTH SHRUG / ROLL (upper & lower)
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.ChinRaiserB,
            0.9f,
            "mouthshru", "lower");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.ChinRaiserT,
            0.9f,
            "mouthshru", "upper");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipSuckLB,
            0.8f,
            "mouthroll", "lower");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipSuckLT,
            0.8f,
            "mouthroll", "lower");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipSuckRB,
            0.8f,
            "mouthroll", "upper");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipSuckRT,
            0.8f,
            "mouthroll", "upper");

        // --------------------
        // UPPER / LOWER LIPS UP/DOWN
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LowerLipDepressorL,
            1.0f,
            "mouthlowerdown_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LowerLipDepressorR,
            1.0f,
            "mouthlowerdown_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.UpperLipRaiserL,
            1.0f,
            "mouthupperup_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.UpperLipRaiserR,
            1.0f,
            "mouthupperup_r");

        // --------------------
        // PUCKER / FUNNEL / TIGHTEN
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipPuckerL,
            1.0f,
            "mouthpucker");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipPuckerR,
            1.0f,
            "mouthpucker");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipFunnelerLB,
            1.0f,
            "mouthfunnel");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipFunnelerLT,
            1.0f,
            "mouthfunnel");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipFunnelerRB,
            1.0f,
            "mouthfunnel");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipFunnelerRT,
            1.0f,
            "mouthfunnel");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipTightenerL,
            0.7f,
            "mouthpress_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LipTightenerR,
            0.7f,
            "mouthpress_r");

        // --------------------
        // MOUTH LEFT / RIGHT
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.MouthLeft,
            1.0f,
            "mouthleft");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.MouthRight,
            1.0f,
            "mouthright");

        // --------------------
        // CHEEKS
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekPuffL,
            1.0f,
            "cheekpuff");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekPuffR,
            1.0f,
            "cheekpuff");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekRaiserL,
            0.9f,
            "cheeksquint_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekRaiserR,
            0.9f,
            "cheeksquint_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekSuckL,
            0.8f,
            "cheeksquint_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.CheekSuckR,
            0.8f,
            "cheeksquint_r");

        // --------------------
        // EYES – BLINK / LID TIGHT / WIDE
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesClosedL,
            1.1f,
            "eyeblink_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesClosedR,
            1.1f,
            "eyeblink_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LidTightenerL,
            0.8f,
            "eyesquint_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.LidTightenerR,
            0.8f,
            "eyesquint_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.UpperLidRaiserL,
            0.9f,
            "eyewide_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.UpperLidRaiserR,
            0.9f,
            "eyewide_r");

        // --------------------
        // EYE LOOK DIRECTIONS
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookUpL,
            1.0f,
            "eyelookup_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookUpR,
            1.0f,
            "eyelookup_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookDownL,
            1.0f,
            "eyelookdown_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookDownR,
            1.0f,
            "eyelookdown_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookLeftL,
            1.0f,
            "eyelookin_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookLeftR,
            1.0f,
            "eyelookout_r");

        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookRightL,
            1.0f,
            "eyelookout_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.EyesLookRightR,
            1.0f,
            "eyelookin_r");

        // --------------------
        // BROWS – UP (INNER & OUTER)
        // --------------------
        int browInnerUp = FindBlendIndex("browinnerup");
        int browOuterUpL = FindBlendIndex("browouterup_l");
        int browOuterUpR = FindBlendIndex("browouterup_r");

        if (browInnerUp >= 0)
        {
            // Inner brow raisers → inner up
            _channels.Add(new Channel
            {
                expression = OVRFaceExpressions.FaceExpression.InnerBrowRaiserL,
                blendIndex = browInnerUp,
                gain = 0.9f
            });
            _channels.Add(new Channel
            {
                expression = OVRFaceExpressions.FaceExpression.InnerBrowRaiserR,
                blendIndex = browInnerUp,
                gain = 0.9f
            });

            // Outer brow raisers
            if (browOuterUpL >= 0)
            {
                _channels.Add(new Channel
                {
                    expression = OVRFaceExpressions.FaceExpression.OuterBrowRaiserL,
                    blendIndex = browOuterUpL,
                    gain = 0.9f
                });
            }
            else
            {
                _channels.Add(new Channel
                {
                    expression = OVRFaceExpressions.FaceExpression.OuterBrowRaiserL,
                    blendIndex = browInnerUp,
                    gain = 0.6f
                });
            }

            if (browOuterUpR >= 0)
            {
                _channels.Add(new Channel
                {
                    expression = OVRFaceExpressions.FaceExpression.OuterBrowRaiserR,
                    blendIndex = browOuterUpR,
                    gain = 0.9f
                });
            }
            else
            {
                _channels.Add(new Channel
                {
                    expression = OVRFaceExpressions.FaceExpression.OuterBrowRaiserR,
                    blendIndex = browInnerUp,
                    gain = 0.6f
                });
            }
        }

        // --------------------
        // BROWS – DOWN
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.BrowLowererL,
            1.0f,
            "browdown_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.BrowLowererR,
            1.0f,
            "browdown_r");

        // --------------------
        // NOSE WRINKLE
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.NoseWrinklerL,
            1.0f,
            "nosesneer_l");
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.NoseWrinklerR,
            1.0f,
            "nosesneer_r");

        // --------------------
        // TONGUE OUT (if available)
        // --------------------
        AddChannelIfFound(
            OVRFaceExpressions.FaceExpression.TongueOut,
            1.0f,
            "tongueout");

        Debug.Log($"[M_LocalFaceDriver] Channel map built with {_channels.Count} channels.");
    }

    #endregion
}