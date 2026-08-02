using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quest guidance: screen arrow toward the active target, destination beacon,
/// and an overhead icon on the escorted villager.
/// </summary>
public sealed class QuestGuidanceUI : MonoBehaviour
{
    const string HealthBarLayerName = "HealthBar";
    const float EscortIconHeight = 2.45f;
    const float EscortIconWorldScale = 0.018f;
    const float BeaconHeight = 3.2f;
    const float BeaconWorldScale = 0.022f;

    [SerializeField] Color arrowColor = new Color(0.35f, 0.95f, 1f, 0.95f);
    [SerializeField] Color escortIconColor = new Color(1f, 0.86f, 0.35f, 1f);
    [SerializeField] Color beaconColor = new Color(0.35f, 0.95f, 1f, 0.95f);
    [SerializeField] float arrowSizePixels = 36f;

    RectTransform _arrowRoot;
    Image _arrowImage;
    GameObject _escortIcon;
    GameObject _beacon;
    Texture2D _triangleTex;
    Sprite _triangleSprite;
    Texture2D _diamondTex;
    Sprite _diamondSprite;
    Texture2D _filledDiamondTex;
    Sprite _filledDiamondSprite;
    Transform _cam;
    float _pulse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<QuestGuidanceUI>() != null)
            return;

        var go = new GameObject("QuestGuidance");
        go.AddComponent<QuestGuidanceUI>();
    }

    void Awake() => BuildUi();

    void OnDestroy()
    {
        DestroyGuidanceObjects();
        if (_triangleSprite != null)
            Destroy(_triangleSprite);
        if (_triangleTex != null)
            Destroy(_triangleTex);
        if (_diamondSprite != null)
            Destroy(_diamondSprite);
        if (_diamondTex != null)
            Destroy(_diamondTex);
        if (_filledDiamondSprite != null)
            Destroy(_filledDiamondSprite);
        if (_filledDiamondTex != null)
            Destroy(_filledDiamondTex);
    }

    void LateUpdate()
    {
        _pulse += Time.deltaTime;
        var quests = QuestService.Instance;
        QuestInstance q = quests != null ? quests.Tracked : null;
        bool active = q != null && q.Status == QuestStatus.Active;

        UpdateArrow(active ? q : null);
        UpdateBeacon(active ? q : null);
        UpdateEscortIcon(active && q.TryGetActiveEscortObjective(out _) ? q : null);
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("QuestGuidanceCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var arrowGo = new GameObject("QuestArrow");
        arrowGo.transform.SetParent(canvasRect, false);
        _arrowImage = arrowGo.AddComponent<Image>();
        _arrowImage.sprite = EnsureTriangleSprite();
        _arrowImage.color = arrowColor;
        _arrowImage.raycastTarget = false;
        _arrowRoot = arrowGo.GetComponent<RectTransform>();
        _arrowRoot.anchorMin = _arrowRoot.anchorMax = new Vector2(0.5f, 0.82f);
        _arrowRoot.pivot = new Vector2(0.5f, 0.5f);
        _arrowRoot.sizeDelta = new Vector2(arrowSizePixels, arrowSizePixels);
        arrowGo.SetActive(false);
    }

    void UpdateArrow(QuestInstance q)
    {
        if (_arrowRoot == null)
            return;

        if (q == null)
        {
            _arrowRoot.gameObject.SetActive(false);
            return;
        }

        Transform player = PlayerReference.TryGetTransform();
        if (player == null)
        {
            _arrowRoot.gameObject.SetActive(false);
            return;
        }

        EnsureCamera();
        Vector3 toTarget = q.TargetPosition - player.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 1f)
        {
            _arrowRoot.gameObject.SetActive(false);
            return;
        }

        Vector3 forward = _cam != null ? _cam.forward : player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;

        float angle = Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up);
        _arrowRoot.localRotation = Quaternion.Euler(0f, 0f, -angle);

        float pulse = 0.85f + 0.15f * Mathf.Sin(_pulse * 4f);
        Color c = arrowColor;
        c.a = arrowColor.a * pulse;
        _arrowImage.color = c;
        _arrowRoot.gameObject.SetActive(true);
    }

    void UpdateBeacon(QuestInstance q)
    {
        if (q == null)
        {
            if (_beacon != null)
                _beacon.SetActive(false);
            return;
        }

        EnsureBeacon();
        Vector3 pos = q.TargetPosition;
        pos.y = SampleGroundY(pos) + BeaconHeight;
        float bob = Mathf.Sin(_pulse * 2.4f) * 0.15f;
        _beacon.transform.position = pos + Vector3.up * bob;
        Billboard(_beacon.transform);

        float pulse = 0.75f + 0.25f * Mathf.Sin(_pulse * 3.5f);
        var img = _beacon.GetComponentInChildren<Image>();
        if (img != null)
        {
            Color c = beaconColor;
            c.a = beaconColor.a * pulse;
            img.color = c;
        }

        _beacon.SetActive(true);
    }

    void UpdateEscortIcon(QuestInstance q)
    {
        if (q == null || !q.TryGetActiveEscortObjective(out QuestObjective step) ||
            step.EscortEntity == Entity.Null)
        {
            if (_escortIcon != null)
                _escortIcon.SetActive(false);
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            if (_escortIcon != null)
                _escortIcon.SetActive(false);
            return;
        }

        EntityManager em = world.EntityManager;
        if (!em.Exists(step.EscortEntity) || !em.HasComponent<LocalTransform>(step.EscortEntity))
        {
            if (_escortIcon != null)
                _escortIcon.SetActive(false);
            return;
        }

        EnsureEscortIcon();
        var lt = em.GetComponentData<LocalTransform>(step.EscortEntity);
        Vector3 pos = new Vector3(lt.Position.x, lt.Position.y, lt.Position.z);
        float bob = Mathf.Sin(_pulse * 3f) * 0.08f;
        _escortIcon.transform.position = pos + Vector3.up * (EscortIconHeight + bob);
        Billboard(_escortIcon.transform);

        float pulse = 0.8f + 0.2f * Mathf.Sin(_pulse * 4f);
        var img = _escortIcon.GetComponentInChildren<Image>();
        if (img != null)
        {
            Color c = escortIconColor;
            c.a = escortIconColor.a * pulse;
            img.color = c;
        }

        _escortIcon.SetActive(true);
    }

    void EnsureBeacon()
    {
        if (_beacon != null)
            return;
        _beacon = CreateWorldMarker("QuestDestinationBeacon", EnsureDiamondSprite(), BeaconWorldScale);
    }

    void EnsureEscortIcon()
    {
        if (_escortIcon != null)
            return;
        _escortIcon = CreateWorldMarker("EscortOverheadIcon", EnsureFilledDiamondSprite(), EscortIconWorldScale);
    }

    GameObject CreateWorldMarker(string name, Sprite sprite, float worldScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 240;

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(64f, 64f);
        go.transform.localScale = Vector3.one * worldScale;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(go.transform, false);
        var image = iconGo.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        var iconRect = image.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        int hb = LayerMask.NameToLayer(HealthBarLayerName);
        if (hb >= 0)
            HierarchyLayers.SetRecursive(go.transform, hb);

        go.SetActive(false);
        return go;
    }

    void DestroyGuidanceObjects()
    {
        if (_escortIcon != null)
            Destroy(_escortIcon);
        if (_beacon != null)
            Destroy(_beacon);
        _escortIcon = null;
        _beacon = null;
    }

    void EnsureCamera()
    {
        if (_cam != null)
            return;
        var main = Camera.main;
        if (main != null)
            _cam = main.transform;
    }

    void Billboard(Transform t)
    {
        EnsureCamera();
        if (_cam == null)
            return;
        Vector3 toCam = _cam.position - t.position;
        if (toCam.sqrMagnitude > 1e-6f)
            t.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    static float SampleGroundY(Vector3 worldPos)
    {
        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen != null && gen.IsTerrainReady)
            return gen.SampleHeightWorldXZ(worldPos.x, worldPos.z);
        return worldPos.y;
    }

    Sprite EnsureTriangleSprite()
    {
        if (_triangleSprite != null)
            return _triangleSprite;

        const int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            name = "QuestArrowTriangleTex",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        // Up-pointing triangle with soft edges.
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res;
                float v = (y + 0.5f) / res;
                float halfWidth = Mathf.Lerp(0.02f, 0.48f, 1f - v);
                float distToEdge = halfWidth - Mathf.Abs(u - 0.5f);
                float a = v > 0.08f && v < 0.96f
                    ? Mathf.Clamp01(distToEdge * res * 0.5f)
                    : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, false);
        _triangleTex = tex;
        _triangleSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
        _triangleSprite.name = "QuestArrowTriangleSprite";
        return _triangleSprite;
    }

    Sprite EnsureDiamondSprite()
    {
        if (_diamondSprite != null)
            return _diamondSprite;

        const int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            name = "QuestDiamondTex",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float cx = (res - 1) * 0.5f;
        float cy = (res - 1) * 0.5f;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = Mathf.Abs(x - cx) / (res * 0.5f);
                float dy = Mathf.Abs(y - cy) / (res * 0.5f);
                float d = dx + dy;
                float a = Mathf.Clamp01((1.05f - d) * res * 0.35f);
                // Hollow-ish core so it reads as a marker, not a blob.
                if (d < 0.28f)
                    a *= 0.15f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, false);
        _diamondTex = tex;
        _diamondSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
        _diamondSprite.name = "QuestDiamondSprite";
        return _diamondSprite;
    }

    Sprite EnsureFilledDiamondSprite()
    {
        if (_filledDiamondSprite != null)
            return _filledDiamondSprite;

        const int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            name = "QuestFilledDiamondTex",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float cx = (res - 1) * 0.5f;
        float cy = (res - 1) * 0.5f;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = Mathf.Abs(x - cx) / (res * 0.5f);
                float dy = Mathf.Abs(y - cy) / (res * 0.5f);
                float d = dx + dy;
                float a = Mathf.Clamp01((1.0f - d) * res * 0.4f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, false);
        _filledDiamondTex = tex;
        _filledDiamondSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
        _filledDiamondSprite.name = "QuestFilledDiamondSprite";
        return _filledDiamondSprite;
    }
}
