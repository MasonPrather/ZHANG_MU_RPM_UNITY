using System.Collections;
using System.IO;
using UnityEngine;
using TMPro;

/// <summary>
/// Watches M_SimpleHttpServer.LastSavedPhotoPath, loads the latest image from disk,
/// pads it to match the target submesh's effective aspect (UV bounds + optional tiling),
/// and applies it to a specific material slot (albedo + emission) on a Renderer.
/// </summary>
public class M_LatestPhotoViewer_Material : MonoBehaviour
{
    [Header("Target Renderer")]
    [Tooltip("MeshRenderer/SkinnedMeshRenderer that contains the screen materials.")]
    public Renderer targetRenderer;

    [Header("Material Selection")]
    [Tooltip("Fallback slot index if name lookup is disabled or fails.")]
    public int fallbackMaterialIndex = 0;

    [Tooltip("If enabled, finds the slot by matching material name substring (case-insensitive).")]
    public bool useMaterialNameLookup = true;

    [Tooltip("Substring match used when useMaterialNameLookup is enabled (e.g., 'paint_05').")]
    public string targetMaterialNameContains = "paint_05";

    [Header("Material Properties")]
    [Tooltip("Albedo texture property name for the shader. Standard uses _MainTex.")]
    public string albedoProperty = "_MainTex";

    [Tooltip("If true, also write the image to emission so the screen glows (prevents old emission overlay).")]
    public bool writeEmission = true;

    [Tooltip("Emission color used when writeEmission is enabled.")]
    public Color emissionColor = Color.white;

    [Header("Aspect / Padding")]
    [Tooltip("If enabled, pads the loaded image so it matches the target submesh's effective aspect ratio.")]
    public bool padToTargetAspect = true;

    [Tooltip("Include material tiling in the aspect computation (tiling.x/tiling.y).")]
    public bool includeMaterialTilingInAspect = true;

    [Tooltip("Final multiplier for small UV quirks (typical range 0.95–1.05).")]
    public float aspectCorrection = 1.0f;

    [Tooltip("If true, padding bars are transparent (alpha=0). Otherwise uses marginColor.")]
    public bool transparentBars = false;

    [Tooltip("Padding bar color when transparentBars is false.")]
    public Color32 marginColor = new Color32(0, 0, 0, 255);

    [Header("Polling")]
    [Tooltip("If enabled, polls M_SimpleHttpServer.LastSavedPhotoPath every frame.")]
    public bool pollFromHttpServer = true;

    [Header("Runtime Apply Mode")]
    [Tooltip("Uses MaterialPropertyBlock (recommended).")]
    public bool useMaterialPropertyBlock = true;

    [Tooltip("Force direct material assignment even if MPB is enabled (useful if batching blocks MPB on device).")]
    public bool forceDirectMaterialSet = false;

    [Header("UI (Optional)")]
    public TMP_Text infoText;

    [Header("Load Robustness")]
    [Tooltip("Retries in case the file is still being written when the viewer tries to read it.")]
    public int maxLoadRetries = 5;

    public float retryDelaySeconds = 0.05f;

    [Header("Debug")]
    public bool verboseLogging = true;

    // Internal state
    private string _lastDisplayedPath;
    private string _currentlyLoadingPath;
    private Coroutine _loadCoroutine;

    private Texture2D _decodedTexture;
    private Texture2D _paddedTexture;
    private Color32[] _padBuffer;
    private MaterialPropertyBlock _mpb;
    private bool _printedSlots;
    private Material _runtimeSlotMaterial = null;
    private int _runtimeSlotIndex = -1;

    private void Update()
    {
        if (!pollFromHttpServer)
            return;

        string latest = M_SimpleHttpServer.LastSavedPhotoPath;
        if (string.IsNullOrEmpty(latest))
            return;

        if (latest == _lastDisplayedPath)
            return;

        if (_currentlyLoadingPath == latest)
            return;

        _lastDisplayedPath = latest;
        _currentlyLoadingPath = latest;

        if (_loadCoroutine != null)
            StopCoroutine(_loadCoroutine);

        _loadCoroutine = StartCoroutine(LoadAndApplyCoroutine(latest));
    }

    /// <summary>
    /// Manually trigger display from a file path.
    /// </summary>
    public void DisplayImageFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        _lastDisplayedPath = path;
        _currentlyLoadingPath = path;

        if (_loadCoroutine != null)
            StopCoroutine(_loadCoroutine);

        _loadCoroutine = StartCoroutine(LoadAndApplyCoroutine(path));
    }

    private IEnumerator LoadAndApplyCoroutine(string path)
    {
        if (targetRenderer == null)
        {
            Debug.LogError("[PhotoViewer] targetRenderer is null.");
            yield break;
        }

        int resolvedSlot = ResolveTargetSlot(targetRenderer);
        if (resolvedSlot < 0)
        {
            Debug.LogError("[PhotoViewer] Could not resolve a valid material slot.");
            yield break;
        }

        int attempts = 0;
        while (attempts++ < maxLoadRetries)
        {
            byte[] bytes = null;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (System.Exception ex)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[PhotoViewer] Read failed (attempt {attempts}): {ex.Message}");
            }

            if (bytes != null && bytes.Length > 0)
            {
                if (_decodedTexture == null)
                    _decodedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

                bool ok = false;
                try
                {
                    ok = _decodedTexture.LoadImage(bytes, markNonReadable: false);
                }
                catch (System.Exception ex)
                {
                    if (verboseLogging)
                        Debug.LogWarning($"[PhotoViewer] LoadImage failed (attempt {attempts}): {ex.Message}");
                }

                if (ok && _decodedTexture.width > 0 && _decodedTexture.height > 0)
                {
                    Texture2D finalTex = _decodedTexture;

                    if (padToTargetAspect)
                    {
                        float targetAspect = GetEffectiveTargetAspect(targetRenderer, resolvedSlot);
                        if (targetAspect > 0.0001f)
                            finalTex = PadToAspect(_decodedTexture, targetAspect, transparentBars, marginColor);
                    }

                    ApplyToRenderer(targetRenderer, resolvedSlot, finalTex);

                    if (infoText != null)
                        infoText.text = $"Displayed: {Path.GetFileName(path)}";

                    if (verboseLogging)
                        Debug.Log($"[PhotoViewer] Applied '{Path.GetFileName(path)}' to slot={resolvedSlot} size={finalTex.width}x{finalTex.height} mpb={(useMaterialPropertyBlock && !forceDirectMaterialSet)}");

                    _loadCoroutine = null;
                    _currentlyLoadingPath = null;
                    yield break;
                }
            }

            yield return new WaitForSeconds(retryDelaySeconds);
        }

        Debug.LogError($"[PhotoViewer] Failed to load image after {maxLoadRetries} attempts: {path}");
        _loadCoroutine = null;
        _currentlyLoadingPath = null;
    }

    private int ResolveTargetSlot(Renderer r)
    {
        var mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return -1;

        if (!_printedSlots && verboseLogging)
        {
            _printedSlots = true;
            for (int i = 0; i < mats.Length; i++)
                Debug.Log($"[PhotoViewer] Slot {i}: '{(mats[i] ? mats[i].name : "NULL")}' shader='{(mats[i] ? mats[i].shader.name : "NULL")}'");
        }

        if (!useMaterialNameLookup || string.IsNullOrEmpty(targetMaterialNameContains))
            return Mathf.Clamp(fallbackMaterialIndex, 0, mats.Length - 1);

        string needle = targetMaterialNameContains.ToLowerInvariant();
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            if (mats[i].name.ToLowerInvariant().Contains(needle))
                return i;
        }

        // Fallback
        return Mathf.Clamp(fallbackMaterialIndex, 0, mats.Length - 1);
    }

    private void ApplyToRenderer(Renderer r, int slot, Texture2D tex)
    {
        if (r == null || tex == null)
            return;

        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        // FORCE MATERIAL INSTANCE (no MPB, no batching)
        var mats = r.materials;
        if (slot < 0 || slot >= mats.Length)
            return;

        Material m = mats[slot];
        if (m == null)
            return;

        // -------- HARD RESET MATERIAL STATE --------

        // Albedo
        if (m.HasProperty("_MainTex"))
            m.SetTexture("_MainTex", tex);

        if (!string.IsNullOrEmpty(albedoProperty) && m.HasProperty(albedoProperty))
            m.SetTexture(albedoProperty, tex);

        // Emission
        if (m.HasProperty("_EmissionMap"))
            m.SetTexture("_EmissionMap", tex);

        if (m.HasProperty("_EmissionColor"))
            m.SetColor("_EmissionColor", emissionColor);

        // Force keyword state
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        // Kill any leftover junk
        if (m.HasProperty("_DetailAlbedoMap"))
            m.SetTexture("_DetailAlbedoMap", null);

        if (m.HasProperty("_DetailNormalMap"))
            m.SetTexture("_DetailNormalMap", null);

        // Force material refresh
        m.SetFloat("_Glossiness", m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0f);
    }

    private void SetTexturesOnMaterial(Material mat, Texture2D tex)
    {
        if (mat == null || tex == null) return;

        // ---- Albedo ----
        if (!string.IsNullOrEmpty(albedoProperty) && mat.HasProperty(albedoProperty))
            mat.SetTexture(albedoProperty, tex);
        else if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", tex);

        // Standard shader also uses _Color to tint albedo; ensure it's neutral
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        // ---- Emission ----
        if (writeEmission)
        {
            if (mat.HasProperty("_EmissionMap"))
                mat.SetTexture("_EmissionMap", tex);

            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emissionColor);

            mat.EnableKeyword("_EMISSION");
        }
        else
        {
            // Force emission OFF so no old texture can "overlay"
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);

            if (mat.HasProperty("_EmissionMap"))
                mat.SetTexture("_EmissionMap", null);

            mat.DisableKeyword("_EMISSION");
        }
    }

    private float GetEffectiveTargetAspect(Renderer r, int slot)
    {
        float uvAspect = GetSubmeshUVAspect(r, slot);
        if (uvAspect <= 0.0001f) uvAspect = 1f;

        float tilingRatio = 1f;
        if (includeMaterialTilingInAspect && r != null)
        {
            var mats = r.sharedMaterials;
            if (mats != null && slot >= 0 && slot < mats.Length && mats[slot] != null)
            {
                Vector2 t = mats[slot].mainTextureScale;
                if (Mathf.Abs(t.y) > 0.0001f)
                    tilingRatio = t.x / t.y;
            }
        }

        return uvAspect * tilingRatio * Mathf.Max(0.0001f, aspectCorrection);
    }

    private float GetSubmeshUVAspect(Renderer r, int slot)
    {
        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return 1f;

        Mesh mesh = mf.sharedMesh;
        var uvs = mesh.uv;
        if (uvs == null || uvs.Length == 0) return 1f;

        int sm = Mathf.Clamp(slot, 0, mesh.subMeshCount - 1);

        int[] tris;
        try { tris = mesh.GetTriangles(sm); }
        catch { return 1f; }

        if (tris == null || tris.Length == 0) return 1f;

        float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
        float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;

        for (int i = 0; i < tris.Length; i++)
        {
            int vi = tris[i];
            if (vi < 0 || vi >= uvs.Length) continue;

            Vector2 uv = uvs[vi];
            if (uv.x < minU) minU = uv.x;
            if (uv.y < minV) minV = uv.y;
            if (uv.x > maxU) maxU = uv.x;
            if (uv.y > maxV) maxV = uv.y;
        }

        float w = maxU - minU;
        float h = maxV - minV;
        if (w <= 0.0001f || h <= 0.0001f) return 1f;

        return w / h;
    }

    private Texture2D PadToAspect(Texture2D src, float targetAspect, bool makeBarsTransparent, Color32 barColor)
    {
        float srcAspect = (float)src.width / src.height;
        if (Mathf.Abs(srcAspect - targetAspect) < 0.0005f)
            return src;

        int outW, outH;
        if (srcAspect > targetAspect)
        {
            outW = src.width;
            outH = Mathf.CeilToInt(src.width / targetAspect);
        }
        else
        {
            outH = src.height;
            outW = Mathf.CeilToInt(src.height * targetAspect);
        }

        if (_paddedTexture == null || _paddedTexture.width != outW || _paddedTexture.height != outH)
        {
            _paddedTexture = new Texture2D(outW, outH, TextureFormat.RGBA32, false, false);
            _paddedTexture.wrapMode = TextureWrapMode.Clamp;
            _paddedTexture.filterMode = FilterMode.Bilinear;
        }

        int outLen = outW * outH;
        if (_padBuffer == null || _padBuffer.Length != outLen)
            _padBuffer = new Color32[outLen];

        Color32 fill = makeBarsTransparent ? new Color32(0, 0, 0, 0) : barColor;
        for (int i = 0; i < outLen; i++)
            _padBuffer[i] = fill;

        Color32[] srcPixels = src.GetPixels32();
        int offsetX = (outW - src.width) / 2;
        int offsetY = (outH - src.height) / 2;

        for (int y = 0; y < src.height; y++)
        {
            int srcRow = y * src.width;
            int dstRow = (y + offsetY) * outW + offsetX;
            for (int x = 0; x < src.width; x++)
                _padBuffer[dstRow + x] = srcPixels[srcRow + x];
        }

        _paddedTexture.SetPixels32(_padBuffer);
        _paddedTexture.Apply(false, false);
        return _paddedTexture;
    }
}