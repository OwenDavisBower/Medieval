using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Floating on-screen joystick: appears at the pointer when a drag starts, knob is clamped to <see cref="maxRadiusPixels"/>.
/// Uses the Input System (mouse primary button + touch) so it works in Editor without UI raycasts.
/// Movement is read by <see cref="PlayerController"/> via <see cref="Axes"/>.
/// </summary>
public class VirtualJoystick : MonoBehaviour
{
    /// <summary>Normalized horizontal (x) and forward (y) input while active; zero when idle.</summary>
    public static Vector2 Axes { get; private set; }

    [SerializeField] float maxRadiusPixels = 72f;
    [SerializeField] float backgroundDiameterPixels = 168f;
    [SerializeField] float knobDiameterPixels = 72f;

    RectTransform _canvasRect;
    RectTransform _root;
    RectTransform _knobRect;

    Vector2 _pressOriginScreen;
    bool _active;
    bool _usingTouch;

    void Awake() => BuildUi();

    void OnDestroy()
    {
        Axes = Vector2.zero;
    }

    void Update()
    {
        var touch = Touchscreen.current;
        var mouse = Mouse.current;

        if (!_active)
        {
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                _usingTouch = true;
                Begin(touch.primaryTouch.position.ReadValue());
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                _usingTouch = false;
                Begin(mouse.position.ReadValue());
            }

            return;
        }

        bool held;
        Vector2 screenPos;
        if (_usingTouch && touch != null)
        {
            held = touch.primaryTouch.press.isPressed;
            screenPos = touch.primaryTouch.position.ReadValue();
        }
        else if (!_usingTouch && mouse != null)
        {
            held = mouse.leftButton.isPressed;
            screenPos = mouse.position.ReadValue();
        }
        else
        {
            End();
            return;
        }

        if (!held)
        {
            End();
            return;
        }

        Vector2 delta = screenPos - _pressOriginScreen;
        Vector2 clamped = Vector2.ClampMagnitude(delta, maxRadiusPixels);
        _knobRect.anchoredPosition = clamped;
        ApplyAxes(maxRadiusPixels > 1e-5f ? clamped / maxRadiusPixels : Vector2.zero);
    }

    void Begin(Vector2 screenPos)
    {
        _active = true;
        _pressOriginScreen = screenPos;
        _root.gameObject.SetActive(true);
        PlaceRootAtScreen(screenPos);
        _knobRect.anchoredPosition = Vector2.zero;
        ApplyAxes(Vector2.zero);
    }

    void End()
    {
        _active = false;
        _root.gameObject.SetActive(false);
        Axes = Vector2.zero;
    }

    static void ApplyAxes(Vector2 normalized)
    {
        if (normalized.sqrMagnitude > 1f)
            normalized.Normalize();
        Axes = normalized;
    }

    void PlaceRootAtScreen(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out Vector2 local);
        _root.anchoredPosition = local;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("VirtualJoystickCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        _canvasRect = canvasGo.GetComponent<RectTransform>();
        _canvasRect.anchorMin = Vector2.zero;
        _canvasRect.anchorMax = Vector2.one;
        _canvasRect.offsetMin = Vector2.zero;
        _canvasRect.offsetMax = Vector2.zero;

        var rootGo = new GameObject("JoystickRoot");
        _root = rootGo.AddComponent<RectTransform>();
        _root.SetParent(_canvasRect, false);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = Vector2.zero;

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(_root, false);
        var bg = bgGo.AddComponent<Image>();
        bg.sprite = CreateUiSprite();
        bg.color = new Color(0.15f, 0.15f, 0.2f, 0.45f);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
        bgRt.sizeDelta = new Vector2(backgroundDiameterPixels, backgroundDiameterPixels);

        var knobGo = new GameObject("Knob");
        knobGo.transform.SetParent(_root, false);
        var kn = knobGo.AddComponent<Image>();
        kn.sprite = CreateUiSprite();
        kn.color = new Color(0.92f, 0.92f, 0.95f, 0.75f);
        _knobRect = knobGo.GetComponent<RectTransform>();
        _knobRect.anchorMin = _knobRect.anchorMax = new Vector2(0.5f, 0.5f);
        _knobRect.sizeDelta = new Vector2(knobDiameterPixels, knobDiameterPixels);

        _root.gameObject.SetActive(false);
    }

    static Sprite CreateUiSprite()
    {
        var tex = Texture2D.whiteTexture;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
