#nullable enable
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Renders the world <see cref="Camera"/> into a low-resolution <see cref="RenderTexture"/> (point filtered),
/// then presents it fullscreen via a second camera + UI canvas (letterboxed to the buffer aspect).
/// Orthographic size on the world camera should follow: bufferHeight / (2 × PPU); this script can apply it at runtime.
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class PixelRtPresentationBootstrap : MonoBehaviour
{
    [SerializeField] Camera worldCamera = null!;
    [Tooltip("If set, this texture is used; otherwise a runtime RT is created from Buffer Size.")]
    [SerializeField] RenderTexture? internalBufferOverride;

    [SerializeField] Vector2Int bufferSize = new(320, 180);
    [SerializeField] [Min(1)] int pixelsPerUnit = 16;
    [SerializeField] bool applyOrthoSizeFromPpu = true;
    [SerializeField] bool letterboxToBufferAspect = true;
    [SerializeField] int presentationCameraDepth = 10;
    [SerializeField] Color presentationClear = Color.black;

    RenderTexture? _ownedRt;
    Camera? _presentCam;
    Canvas? _canvas;
    RawImage? _rawImage;

    void Awake()
    {
        if (worldCamera == null)
            return;

        worldCamera.enabled = true;
        worldCamera.forceIntoRenderTexture = true;
        worldCamera.allowMSAA = false;
        worldCamera.allowHDR = false;
        worldCamera.allowDynamicResolution = false;

        var worldUrp = worldCamera.GetUniversalAdditionalCameraData();
        worldUrp.renderPostProcessing = false;
        worldUrp.antialiasing = AntialiasingMode.None;
        worldUrp.dithering = false;

        if (applyOrthoSizeFromPpu && worldCamera.orthographic)
            worldCamera.orthographicSize = bufferSize.y / (2f * pixelsPerUnit);

        RenderTexture rt = internalBufferOverride != null
            ? internalBufferOverride
            : CreateOwnedRt();

        // For an override RT, we can safely enforce sampling params, but not creation-time flags
        // (Unity throws if you try to change mipmap/MSAA settings after the RT is created).
        rt.filterMode = FilterMode.Point;
        rt.wrapMode = TextureWrapMode.Clamp;
        if (internalBufferOverride != null)
        {
            if (rt.useMipMap || rt.autoGenerateMips || rt.antiAliasing != 1)
            {
                Debug.LogWarning(
                    "PixelRtPresentationBootstrap: internalBufferOverride RenderTexture should be configured with " +
                    "useMipMap=false, autoGenerateMips=false, antiAliasing=1 (set these in the asset/creator; they can't be changed at runtime once created).",
                    this);
            }
        }

        bufferSize = new Vector2Int(rt.width, rt.height);

        worldCamera.targetTexture = rt;
        EnsurePresentation(rt);

        if (_rawImage == null || _rawImage.texture == null)
            Debug.LogError($"PixelRtPresentationBootstrap: RawImage presentation not created (rt={rt.width}x{rt.height}).", this);
    }

    void LateUpdate()
    {
        if (_rawImage == null || !letterboxToBufferAspect)
            return;

        // Integer scaling prevents uneven pixel sizes / blur from fractional UI scaling.
        int w = Mathf.Max(1, bufferSize.x);
        int h = Mathf.Max(1, bufferSize.y);
        int sx = Mathf.Max(1, Screen.width / w);
        int sy = Mathf.Max(1, Screen.height / h);
        int s = Mathf.Max(1, Mathf.Min(sx, sy));

        RectTransform r = _rawImage.rectTransform;
        r.anchorMin = new Vector2(0.5f, 0.5f);
        r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w * s, h * s);
        r.anchoredPosition = Vector2.zero;
    }

    void OnDestroy()
    {
        if (_ownedRt != null)
        {
            if (worldCamera != null && worldCamera.targetTexture == _ownedRt)
                worldCamera.targetTexture = null;
            Destroy(_ownedRt);
            _ownedRt = null;
        }

        if (_presentCam != null)
        {
            Destroy(_presentCam.gameObject);
            _presentCam = null;
        }

        _canvas = null;
        _rawImage = null;
    }

    RenderTexture CreateOwnedRt()
    {
        var rt = new RenderTexture(bufferSize.x, bufferSize.y, 24, RenderTextureFormat.ARGB32)
        {
            name = "PixelGameBuffer_Runtime",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rt.Create();
        _ownedRt = rt;
        return rt;
    }

    void EnsurePresentation(RenderTexture source)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
            uiLayer = 0;

        var camGo = new GameObject("PixelPresentCamera");
        camGo.transform.SetParent(null, false);
        camGo.layer = uiLayer;

        _presentCam = camGo.AddComponent<Camera>();
        _presentCam.clearFlags = CameraClearFlags.SolidColor;
        _presentCam.backgroundColor = presentationClear;
        // Only used to clear the backbuffer (the actual game view is UI overlay).
        _presentCam.cullingMask = 0;
        _presentCam.depth = presentationCameraDepth;
        _presentCam.orthographic = true;
        _presentCam.orthographicSize = 5f;
        _presentCam.nearClipPlane = 0.01f;
        _presentCam.farClipPlane = 50f;
        _presentCam.useOcclusionCulling = false;
        _presentCam.allowHDR = false;
        _presentCam.allowMSAA = false;

        var presentUrp = _presentCam.GetUniversalAdditionalCameraData();
        presentUrp.renderType = CameraRenderType.Base;
        presentUrp.renderPostProcessing = false;
        presentUrp.antialiasing = AntialiasingMode.None;
        presentUrp.dithering = false;
        presentUrp.volumeLayerMask = 0;

        var canvasGo = new GameObject("PixelPresentCanvas");
        canvasGo.layer = uiLayer;
        canvasGo.transform.SetParent(camGo.transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        // Overlay is the most robust: it can't be culled by camera masks and avoids camera-space scaling issues.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.pixelPerfect = true;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 32767;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.referencePixelsPerUnit = 100f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var rawGo = new GameObject("GameBufferView");
        rawGo.layer = uiLayer;
        rawGo.transform.SetParent(canvasGo.transform, false);

        var rect = rawGo.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        _rawImage = rawGo.AddComponent<RawImage>();
        _rawImage.texture = source;
        _rawImage.raycastTarget = false;
        _rawImage.color = Color.white;
    }
}
