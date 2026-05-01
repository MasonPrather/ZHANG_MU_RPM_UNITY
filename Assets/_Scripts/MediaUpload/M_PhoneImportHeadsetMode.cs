using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the phone photo import flow usable without removing the headset.
/// It shows a large join prompt in VR, enables Quest passthrough when available,
/// and temporarily clears opaque scene renderers so the user can look down at a physical phone.
/// </summary>
public class M_PhoneImportHeadsetMode : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("If true, enter phone import mode as soon as the scene starts.")]
    public bool activateOnStart = true;

    [Tooltip("If true, hide the phone prompt after the first phone upload is detected.")]
    public bool closePromptAfterFirstUpload = true;

    [Tooltip("Seconds to keep the prompt visible after the first upload arrives.")]
    public float closeDelayAfterUploadSeconds = 2f;

    [Tooltip("If true, passthrough stays enabled after the prompt closes.")]
    public bool keepPassthroughAfterUpload = false;

    [Header("References")]
    public M_ServerBootstrap serverBootstrap;
    public M_PhoneUploadToDisplay phoneUploadBridge;
    public Camera xrCamera;
    public OVRManager ovrManager;
    public OVRPassthroughLayer passthroughLayer;

    [Header("UI")]
    [Tooltip("Root object for an existing prompt. If empty, one is generated at runtime.")]
    public GameObject promptRoot;

    public TMP_Text titleText;
    public TMP_Text instructionsText;
    public TMP_Text statusText;

    [Tooltip("If no prompt text is assigned, create a simple world-space prompt at runtime.")]
    public bool createPromptIfMissing = true;

    [Tooltip("Maximum URLs shown in the headset prompt.")]
    public int maxDisplayedUrls = 2;

    [Header("Placement")]
    [Tooltip("Distance in front of the headset where the generated prompt appears.")]
    public float promptDistanceMeters = 1.35f;

    [Tooltip("Prompt offset in headset-local meters.")]
    public Vector3 promptOffsetMeters = new Vector3(0f, 0.14f, 0f);

    [Tooltip("If true, place the prompt in front of the headset when import mode starts.")]
    public bool recenterPromptOnEnter = true;

    [Tooltip("If true, keep the prompt rotated toward the headset while it is visible.")]
    public bool billboardPromptToHeadset = true;

    [Header("Passthrough")]
    public bool enablePassthrough = true;

    [Tooltip("If true, disable regular scene renderers during phone mode so passthrough can be seen.")]
    public bool hideSceneRenderersDuringPhoneMode = true;

    [Tooltip("Objects that should keep their renderers while phone mode hides the rest of the scene.")]
    public GameObject[] rendererRootsToKeepVisible;

    [Header("Debug")]
    public bool verboseLogging = true;

    private readonly List<RendererState> _hiddenRenderers = new List<RendererState>();
    private bool _isPhoneModeActive;
    private bool _createdPrompt;
    private bool _previousManagerPassthroughEnabled;
    private bool _previousBoundarySuppressed;
    private bool _previousPassthroughLayerEnabled;
    private bool _previousPassthroughLayerHidden;
    private float _previousPassthroughOpacity = 1f;
    private CameraClearFlags _previousCameraClearFlags;
    private Color _previousCameraBackgroundColor;
    private string _lastObservedUploadPath;
    private Coroutine _closePromptCoroutine;

    private struct RendererState
    {
        public Renderer renderer;
        public bool enabled;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeServerEvents();
    }

    private void Start()
    {
        StartCoroutine(StartCoroutineDeferred());
    }

    private void OnDisable()
    {
        UnsubscribeServerEvents();

        if (_isPhoneModeActive)
            ExitPhoneImportMode();
    }

    private void OnDestroy()
    {
        UnsubscribeServerEvents();
    }

    private IEnumerator StartCoroutineDeferred()
    {
        yield return null;

        ResolveReferences();
        SubscribeServerEvents();
        EnsurePrompt();
        RefreshPromptText();

        _lastObservedUploadPath = M_SimpleHttpServer.LastSavedPhotoPath;

        if (activateOnStart)
            EnterPhoneImportMode();
    }

    private void Update()
    {
        if (!_isPhoneModeActive || !closePromptAfterFirstUpload)
            return;

        string uploadPath = M_SimpleHttpServer.LastSavedPhotoPath;
        if (string.IsNullOrEmpty(uploadPath) ||
            string.Equals(uploadPath, _lastObservedUploadPath, StringComparison.Ordinal))
        {
            return;
        }

        _lastObservedUploadPath = uploadPath;
        SetStatus("Photo received. Opening gallery...");

        if (_closePromptCoroutine != null)
            StopCoroutine(_closePromptCoroutine);

        _closePromptCoroutine = StartCoroutine(ClosePromptAfterDelay());
    }

    private void LateUpdate()
    {
        if (!_isPhoneModeActive || !billboardPromptToHeadset || promptRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 direction = promptRoot.transform.position - xrCamera.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
            promptRoot.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public void EnterPhoneImportMode()
    {
        if (_isPhoneModeActive)
        {
            RefreshPromptText();
            return;
        }

        ResolveReferences();
        EnsurePrompt();

        _isPhoneModeActive = true;

        if (promptRoot != null)
            promptRoot.SetActive(true);

        if (recenterPromptOnEnter)
            PlacePromptInFrontOfHeadset();

        RefreshPromptText();
        ApplyPassthroughMode();
        HideSceneRenderers();

        SetStatus("Look down through passthrough and use your phone.");

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] Phone import mode entered.");
    }

    public void ExitPhoneImportMode()
    {
        if (!_isPhoneModeActive)
            return;

        _isPhoneModeActive = false;

        if (_closePromptCoroutine != null)
        {
            StopCoroutine(_closePromptCoroutine);
            _closePromptCoroutine = null;
        }

        RestoreSceneRenderers();

        if (!keepPassthroughAfterUpload)
            RestorePassthroughMode();

        if (promptRoot != null)
            promptRoot.SetActive(false);

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] Phone import mode exited.");
    }

    public void RefreshPromptText()
    {
        ResolveReferences();
        EnsurePrompt();

        if (titleText != null)
            titleText.text = "Phone Import";

        if (instructionsText != null)
            instructionsText.text = BuildHeadsetInstructions();
    }

    private IEnumerator ClosePromptAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, closeDelayAfterUploadSeconds));
        _closePromptCoroutine = null;
        ExitPhoneImportMode();
    }

    private void ResolveReferences()
    {
        if (serverBootstrap == null)
            serverBootstrap = UnityEngine.Object.FindObjectOfType<M_ServerBootstrap>();

        if (phoneUploadBridge == null)
            phoneUploadBridge = UnityEngine.Object.FindObjectOfType<M_PhoneUploadToDisplay>();

        ResolveCamera();

        if (ovrManager == null)
            ovrManager = OVRManager.instance != null
                ? OVRManager.instance
                : UnityEngine.Object.FindObjectOfType<OVRManager>();

        if (passthroughLayer == null)
            passthroughLayer = UnityEngine.Object.FindObjectOfType<OVRPassthroughLayer>();
    }

    private void ResolveCamera()
    {
        if (xrCamera != null)
            return;

        xrCamera = OVRManager.FindMainCamera();

        if (xrCamera == null)
            xrCamera = Camera.main;
    }

    private void SubscribeServerEvents()
    {
        if (serverBootstrap == null)
            return;

        serverBootstrap.InstructionsPublished -= HandleInstructionsPublished;
        serverBootstrap.InstructionsPublished += HandleInstructionsPublished;
    }

    private void UnsubscribeServerEvents()
    {
        if (serverBootstrap != null)
            serverBootstrap.InstructionsPublished -= HandleInstructionsPublished;
    }

    private void HandleInstructionsPublished(M_ServerBootstrap bootstrap)
    {
        RefreshPromptText();
    }

    private string BuildHeadsetInstructions()
    {
        if (serverBootstrap == null)
            return "Starting phone import...\n\nKeep the phone and headset on the same Wi-Fi network.";

        string[] urls = serverBootstrap.PublishedUploadUrls;
        string code = serverBootstrap.EffectivePairingCode;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("On your phone, open:");

        if (urls == null || urls.Length == 0)
        {
            sb.AppendLine($"http://<quest-ip>:{serverBootstrap.httpPort}");
        }
        else
        {
            int count = Mathf.Min(Mathf.Max(1, maxDisplayedUrls), urls.Length);
            for (int i = 0; i < count; i++)
                sb.AppendLine(urls[i]);
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            sb.AppendLine();
            sb.AppendLine("Code:");
            sb.AppendLine(FormatCodeForReading(code));
        }

        sb.AppendLine();
        sb.Append("Choose photos on the phone. They will appear here.");
        return sb.ToString();
    }

    private static string FormatCodeForReading(string code)
    {
        string trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length <= 3)
            return trimmed;

        StringBuilder sb = new StringBuilder(trimmed.Length + trimmed.Length / 3);
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (i > 0 && i % 3 == 0)
                sb.Append(' ');

            sb.Append(trimmed[i]);
        }

        return sb.ToString();
    }

    private void SetStatus(string value)
    {
        if (statusText != null)
            statusText.text = value;

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] " + value);
    }

    private void EnsurePrompt()
    {
        if (instructionsText != null && promptRoot != null)
            return;

        if (!createPromptIfMissing || _createdPrompt)
            return;

        CreateGeneratedPrompt();
    }

    private void CreateGeneratedPrompt()
    {
        GameObject canvasObject = new GameObject("PhoneImportHeadsetPrompt");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(980f, 640f);
        canvasRect.localScale = Vector3.one * 0.0018f;

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.03f, 0.035f, 0.04f, 0.92f);

        titleText = CreateText(canvasRect, "Title", new Vector2(0f, 218f), new Vector2(880f, 92f), 58f, FontStyles.Bold);
        instructionsText = CreateText(canvasRect, "Instructions", new Vector2(0f, -10f), new Vector2(880f, 350f), 38f, FontStyles.Normal);
        statusText = CreateText(canvasRect, "Status", new Vector2(0f, -256f), new Vector2(880f, 62f), 26f, FontStyles.Italic);

        promptRoot = canvasObject;
        _createdPrompt = true;

        PlacePromptInFrontOfHeadset();
    }

    private static TMP_Text CreateText(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.margin = new Vector4(10f, 4f, 10f, 4f);

        return text;
    }

    private void PlacePromptInFrontOfHeadset()
    {
        if (promptRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 localOffset = promptOffsetMeters;
        localOffset.z += Mathf.Max(0.4f, promptDistanceMeters);
        promptRoot.transform.position = xrCamera.transform.TransformPoint(localOffset);
        promptRoot.transform.rotation = Quaternion.LookRotation(promptRoot.transform.position - xrCamera.transform.position, Vector3.up);
    }

    private void ApplyPassthroughMode()
    {
        if (!enablePassthrough)
            return;

        ResolveReferences();

        if (ovrManager == null)
        {
            Debug.LogWarning("[M_PhoneImportHeadsetMode] No OVRManager found; passthrough prompt will still work without passthrough.");
            return;
        }

        _previousManagerPassthroughEnabled = ovrManager.isInsightPassthroughEnabled;
        _previousBoundarySuppressed = ovrManager.shouldBoundaryVisibilityBeSuppressed;

        ovrManager.isInsightPassthroughEnabled = true;
        ovrManager.shouldBoundaryVisibilityBeSuppressed = false;

        EnsurePassthroughLayer();

        if (passthroughLayer != null)
        {
            _previousPassthroughLayerEnabled = passthroughLayer.enabled;
            _previousPassthroughLayerHidden = passthroughLayer.hidden;
            _previousPassthroughOpacity = passthroughLayer.textureOpacity;

            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            passthroughLayer.textureOpacity = 1f;
            passthroughLayer.hidden = false;
            passthroughLayer.enabled = true;
        }

        if (xrCamera != null)
        {
            _previousCameraClearFlags = xrCamera.clearFlags;
            _previousCameraBackgroundColor = xrCamera.backgroundColor;
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
    }

    private void RestorePassthroughMode()
    {
        if (!enablePassthrough)
            return;

        if (passthroughLayer != null)
        {
            passthroughLayer.textureOpacity = _previousPassthroughOpacity;
            passthroughLayer.hidden = _previousPassthroughLayerHidden;
            passthroughLayer.enabled = _previousPassthroughLayerEnabled;
        }

        if (ovrManager != null)
        {
            ovrManager.isInsightPassthroughEnabled = _previousManagerPassthroughEnabled;
            ovrManager.shouldBoundaryVisibilityBeSuppressed = _previousBoundarySuppressed;
        }

        if (xrCamera != null)
        {
            xrCamera.clearFlags = _previousCameraClearFlags;
            xrCamera.backgroundColor = _previousCameraBackgroundColor;
        }
    }

    private void EnsurePassthroughLayer()
    {
        if (passthroughLayer != null)
            return;

        GameObject target = ovrManager != null ? ovrManager.gameObject : gameObject;
        passthroughLayer = target.AddComponent<OVRPassthroughLayer>();
        passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
        passthroughLayer.textureOpacity = 1f;
    }

    private void HideSceneRenderers()
    {
        if (!hideSceneRenderersDuringPhoneMode || _hiddenRenderers.Count > 0)
            return;

        Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || ShouldKeepRendererVisible(renderer))
                continue;

            _hiddenRenderers.Add(new RendererState
            {
                renderer = renderer,
                enabled = renderer.enabled
            });

            renderer.enabled = false;
        }
    }

    private void RestoreSceneRenderers()
    {
        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            RendererState state = _hiddenRenderers[i];
            if (state.renderer != null)
                state.renderer.enabled = state.enabled;
        }

        _hiddenRenderers.Clear();
    }

    private bool ShouldKeepRendererVisible(Renderer renderer)
    {
        if (renderer == null)
            return true;

        Transform rendererTransform = renderer.transform;

        if (promptRoot != null && rendererTransform.IsChildOf(promptRoot.transform))
            return true;

        if (rendererRootsToKeepVisible != null)
        {
            for (int i = 0; i < rendererRootsToKeepVisible.Length; i++)
            {
                GameObject root = rendererRootsToKeepVisible[i];
                if (root != null && rendererTransform.IsChildOf(root.transform))
                    return true;
            }
        }

        return false;
    }
}
