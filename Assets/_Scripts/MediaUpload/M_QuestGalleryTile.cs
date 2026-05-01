using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Single gallery tile entry.
/// Displays a thumbnail + filename and notifies the controller when pressed.
/// </summary>
public class M_QuestGalleryTile : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, ISubmitHandler
{
    [Header("UI References")]
    [Tooltip("Button used to select this tile.")]
    public Button button;

    [Tooltip("RawImage used to display the thumbnail.")]
    public RawImage thumbnailImage;

    [Tooltip("Optional text label for the file name.")]
    public TMP_Text fileNameText;

    [Tooltip("Optional selected-state object.")]
    public GameObject selectedVisual;

    [Header("Fallbacks")]
    [Tooltip("Optional placeholder texture shown before thumbnail load.")]
    public Texture placeholderTexture;

    [Header("Debug")]
    [Tooltip("If true, log tile events.")]
    public bool verboseLogging = false;

    private M_QuestGalleryAndroidBridge.GalleryItem _item;
    private M_QuestGalleryController _controller;
    private Texture2D _runtimeThumbnail;
    private int _lastPressFrame = -1;

    /// <summary>
    /// Initializes this tile with data and controller callback.
    /// </summary>
    public void Setup(M_QuestGalleryController controller, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        _controller = controller;
        _item = item;

        if (fileNameText != null)
            fileNameText.text = item != null ? item.fileName : "(null)";

        if (thumbnailImage != null)
        {
            thumbnailImage.texture = placeholderTexture;
            thumbnailImage.color = placeholderTexture != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        }

        if (button != null)
        {
            button.onClick.RemoveListener(OnPressed);
            button.onClick.AddListener(OnPressed);

            if (button.targetGraphic != null)
                button.targetGraphic.raycastTarget = true;
        }

        DisableDecorativeRaycasts();

        SetSelected(false);

        if (verboseLogging && item != null)
            Debug.Log($"[M_QuestGalleryTile] Setup -> {item.fileName}");
    }

    /// <summary>
    /// Applies the generated thumbnail to this tile.
    /// </summary>
    public void SetThumbnail(Texture2D thumbnail)
    {
        if (_runtimeThumbnail != null && _runtimeThumbnail != thumbnail)
            Destroy(_runtimeThumbnail);

        _runtimeThumbnail = thumbnail;

        if (thumbnailImage != null)
        {
            thumbnailImage.texture = _runtimeThumbnail != null ? _runtimeThumbnail : placeholderTexture;
            thumbnailImage.color = Color.white;
        }
    }

    /// <summary>
    /// Updates selected-state visuals.
    /// </summary>
    public void SetSelected(bool isSelected)
    {
        if (selectedVisual != null)
            selectedVisual.SetActive(isSelected);
    }

    private void OnPressed()
    {
        if (_lastPressFrame == Time.frameCount)
            return;

        _lastPressFrame = Time.frameCount;

        if (verboseLogging && _item != null)
            Debug.Log($"[M_QuestGalleryTile] Pressed -> {_item.fileName}");

        _controller?.OnTileSelected(this, _item);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnPressed();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnPressed();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        OnPressed();
    }

    private void DisableDecorativeRaycasts()
    {
        Graphic buttonGraphic = button != null ? button.targetGraphic : null;
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null || graphics[i] == buttonGraphic)
                continue;

            graphics[i].raycastTarget = false;
        }
    }

    private void OnDestroy()
    {
        if (_runtimeThumbnail != null)
            Destroy(_runtimeThumbnail);
    }
}
