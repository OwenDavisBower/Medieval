using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right screen minimap baked from terrain height + splat (paths / rock / water) with village and player markers.
/// Village markers use live <see cref="SettlementBuilder"/> world centers when available (not nominal plan points).
/// Follows the same runtime uGUI pattern as <see cref="VirtualJoystick"/>.
/// Spawns itself at play mode start so scene script GUID wiring is not required.
/// </summary>
public class MinimapUI : MonoBehaviour
{
    [SerializeField] float sizePixels = 240f;
    [SerializeField] float marginPixels = 20f;
    [SerializeField] float borderPixels = 3f;
    [SerializeField] int textureResolution = 256;
    [Tooltip("1 = full world. 1.5 = zoomed in 50%.")]
    [SerializeField] float zoom = 1.5f;
    [SerializeField] float villageMarkerRadiusTexels = 3.5f;
    [SerializeField] float playerMarkerSizePixels = 12f;
    [Tooltip("Blend to water where height is this far below TerrainGenerator.baseHeight.")]
    [SerializeField] float waterDepthBlend = 0.35f;
    [SerializeField] Color borderColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
    [SerializeField] Color panelColor = new Color(0.05f, 0.06f, 0.07f, 0.55f);
    [SerializeField] Color grassLowColor = new Color(0.32f, 0.42f, 0.2f, 1f);
    [SerializeField] Color grassHighColor = new Color(0.48f, 0.56f, 0.28f, 1f);
    [SerializeField] Color pathColor = new Color(0.56f, 0.48f, 0.32f, 1f);
    [SerializeField] Color rockColor = new Color(0.62f, 0.62f, 0.62f, 1f);
    [SerializeField] Color mountainColor = new Color(0.78f, 0.78f, 0.8f, 1f);
    [SerializeField] Color waterColor = new Color(0.22f, 0.42f, 0.72f, 1f);
    [SerializeField] Color villageColor = new Color(0.92f, 0.72f, 0.22f, 1f);
    [SerializeField] Color playerColor = new Color(0.95f, 0.25f, 0.2f, 1f);
    [SerializeField] Color questTargetColor = new Color(0.3f, 0.95f, 1f, 1f);
    [SerializeField] float questMarkerSizePixels = 16f;

    [SerializeField] TerrainGenerator terrainGenerator;
    [SerializeField] WorldGenerationCoordinator worldGeneration;

    RectTransform _mapRect;
    RawImage _mapImage;
    RectTransform _playerMarker;
    RectTransform _questMarker;
    Image _questMarkerImage;
    Texture2D _mapTexture;
    Color32[] _pixels;
    Texture2D _circleTexture;
    Sprite _circleSprite;
    Texture2D _diamondTexture;
    Sprite _diamondSprite;
    float _questPulse;

    bool _rebuildQueued;
    TerrainGenerator _boundTerrain;
    bool _subscribedSettlements;
    readonly List<Vector3> _lastPaintedSettlementCenters = new List<Vector3>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<MinimapUI>() != null)
            return;

        var go = new GameObject("Minimap");
        go.AddComponent<MinimapUI>();
    }

    void Awake() => BuildUi();

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
        TerrainGenerator.SplatmapChanged += OnSplatmapChanged;
        WorldGenerationCoordinator.WorldContentPlanned += OnWorldContentPlanned;
        TrySubscribeSettlements();
        QueueRebuild();
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
        TerrainGenerator.SplatmapChanged -= OnSplatmapChanged;
        WorldGenerationCoordinator.WorldContentPlanned -= OnWorldContentPlanned;
        UnsubscribeSettlements();
    }

    void OnDestroy()
    {
        if (_mapTexture != null)
            Destroy(_mapTexture);
        if (_circleSprite != null)
            Destroy(_circleSprite);
        if (_circleTexture != null)
            Destroy(_circleTexture);
        if (_diamondSprite != null)
            Destroy(_diamondSprite);
        if (_diamondTexture != null)
            Destroy(_diamondTexture);
    }

    void LateUpdate()
    {
        TrySubscribeSettlements();

        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            RebuildMapTexture();
        }

        _questPulse += Time.deltaTime;
        UpdatePlayerMarker();
        UpdateQuestMarker();
    }

    void OnTerrainGenerated(TerrainGenerator _) => QueueRebuild();

    void OnSplatmapChanged(TerrainGenerator _) => QueueRebuild();

    void OnWorldContentPlanned() => QueueRebuild();

    void OnSettlementsChanged()
    {
        // Stock/reputation churn also raises Changed; only rebake when centers moved.
        if (SettlementCentersChanged())
            QueueRebuild();
    }

    void TrySubscribeSettlements()
    {
        if (_subscribedSettlements)
            return;
        if (SettlementService.Instance == null)
            return;

        SettlementService.Instance.Changed += OnSettlementsChanged;
        _subscribedSettlements = true;
        if (SettlementCentersChanged())
            QueueRebuild();
    }

    void UnsubscribeSettlements()
    {
        if (!_subscribedSettlements)
            return;
        if (SettlementService.Instance != null)
            SettlementService.Instance.Changed -= OnSettlementsChanged;
        _subscribedSettlements = false;
    }

    bool SettlementCentersChanged()
    {
        var settlements = SettlementService.Instance != null
            ? SettlementService.Instance.Settlements
            : null;
        int count = settlements != null ? settlements.Count : 0;
        if (count != _lastPaintedSettlementCenters.Count)
            return true;

        const float epsSq = 0.01f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = settlements[i].WorldCenter;
            Vector3 b = _lastPaintedSettlementCenters[i];
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            if (dx * dx + dz * dz > epsSq)
                return true;
        }

        return false;
    }

    void QueueRebuild() => _rebuildQueued = true;

    void BuildUi()
    {
        var canvasGo = new GameObject("MinimapCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var panelGo = new GameObject("MinimapPanel");
        panelGo.transform.SetParent(canvasRect, false);
        var panelImage = panelGo.AddComponent<Image>();
        panelImage.color = panelColor;
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(sizePixels + borderPixels * 2f, sizePixels + borderPixels * 2f);
        panelRect.anchoredPosition = new Vector2(-marginPixels, -marginPixels);

        var borderGo = new GameObject("Border");
        borderGo.transform.SetParent(panelRect, false);
        var borderImage = borderGo.AddComponent<Image>();
        borderImage.color = borderColor;
        var borderRect = borderGo.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        var mapGo = new GameObject("Map");
        mapGo.transform.SetParent(borderRect, false);
        _mapImage = mapGo.AddComponent<RawImage>();
        _mapImage.color = Color.white;
        _mapRect = mapGo.GetComponent<RectTransform>();
        _mapRect.anchorMin = Vector2.zero;
        _mapRect.anchorMax = Vector2.one;
        _mapRect.offsetMin = new Vector2(borderPixels, borderPixels);
        _mapRect.offsetMax = new Vector2(-borderPixels, -borderPixels);

        var playerGo = new GameObject("PlayerMarker");
        playerGo.transform.SetParent(_mapRect, false);
        var playerImage = playerGo.AddComponent<Image>();
        playerImage.sprite = EnsureCircleSprite();
        playerImage.color = playerColor;
        _playerMarker = playerGo.GetComponent<RectTransform>();
        _playerMarker.anchorMin = _playerMarker.anchorMax = new Vector2(0.5f, 0.5f);
        _playerMarker.pivot = new Vector2(0.5f, 0.5f);
        _playerMarker.sizeDelta = new Vector2(playerMarkerSizePixels, playerMarkerSizePixels);
        _playerMarker.gameObject.SetActive(false);

        var questGo = new GameObject("QuestTargetMarker");
        questGo.transform.SetParent(_mapRect, false);
        _questMarkerImage = questGo.AddComponent<Image>();
        _questMarkerImage.sprite = EnsureDiamondSprite();
        _questMarkerImage.color = questTargetColor;
        _questMarkerImage.raycastTarget = false;
        _questMarker = questGo.GetComponent<RectTransform>();
        _questMarker.anchorMin = _questMarker.anchorMax = new Vector2(0.5f, 0.5f);
        _questMarker.pivot = new Vector2(0.5f, 0.5f);
        _questMarker.sizeDelta = new Vector2(questMarkerSizePixels, questMarkerSizePixels);
        questGo.SetActive(false);

        // Player sits above the quest pin so both stay readable when overlapping.
        _playerMarker.SetAsLastSibling();

        EnsureMapTexture();
    }

    const int CircleTextureResolution = 64;

    Sprite EnsureCircleSprite()
    {
        if (_circleSprite != null)
            return _circleSprite;

        var tex = new Texture2D(CircleTextureResolution, CircleTextureResolution, TextureFormat.RGBA32, false);
        tex.name = "MinimapPlayerCircleTex";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float cx = (CircleTextureResolution - 1) * 0.5f;
        float cy = (CircleTextureResolution - 1) * 0.5f;
        float radius = CircleTextureResolution * 0.5f - 0.5f;
        const float edgePx = 1.25f;

        for (int y = 0; y < CircleTextureResolution; y++)
        {
            for (int x = 0; x < CircleTextureResolution; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((radius + edgePx - dist) / edgePx);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, false);
        _circleTexture = tex;
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, CircleTextureResolution, CircleTextureResolution),
            new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.Tight);
        _circleSprite.name = "MinimapPlayerCircleSprite";
        return _circleSprite;
    }

    Sprite EnsureDiamondSprite()
    {
        if (_diamondSprite != null)
            return _diamondSprite;

        const int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            name = "MinimapQuestDiamondTex",
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
                if (d < 0.22f)
                    a *= 0.2f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, false);
        _diamondTexture = tex;
        _diamondSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
        _diamondSprite.name = "MinimapQuestDiamondSprite";
        return _diamondSprite;
    }

    void EnsureMapTexture()
    {
        int res = Mathf.Clamp(textureResolution, 64, 512);
        if (_mapTexture != null && _mapTexture.width == res && _mapTexture.height == res)
            return;

        if (_mapTexture != null)
            Destroy(_mapTexture);

        _mapTexture = new Texture2D(res, res, TextureFormat.RGBA32, false, false)
        {
            name = "MinimapTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };
        _pixels = new Color32[res * res];
        if (_mapImage != null)
            _mapImage.texture = _mapTexture;
    }

    void RebuildMapTexture()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady || gen.SplatmapTexture == null)
            return;

        EnsureMapTexture();
        _boundTerrain = gen;

        int res = _mapTexture.width;
        float worldSize = gen.worldSize;
        Vector3 origin = gen.transform.position;
        float viewSize = GetViewSize(worldSize);
        float baseH = gen.baseHeight;
        float maxH = Mathf.Max(1e-3f, gen.maxHeightVariation);

        var heights = new NativeArray<float>(gen.HeightmapTexelCount, Allocator.Temp);
        try
        {
            if (!gen.TryCopyHeightmap(heights))
                return;

            int hr = gen.worldResolution;
            var splat = gen.SplatmapTexture.GetPixelData<Color>(0);
            int sr = gen.SplatmapTexture.width;

            for (int y = 0; y < res; y++)
            {
                float v = (y + 0.5f) / res;
                float wz = origin.z + (v - 0.5f) * viewSize;
                for (int x = 0; x < res; x++)
                {
                    float u = (x + 0.5f) / res;
                    float wx = origin.x + (u - 0.5f) * viewSize;

                    float h = SampleHeightBilinear(heights, hr, origin, worldSize, wx, wz);
                    float height01 = Mathf.Clamp01((h - baseH) / maxH);

                    Color grass = Color.Lerp(grassLowColor, grassHighColor, height01);
                    Color c = Color.Lerp(grass, mountainColor, Mathf.SmoothStep(0.45f, 0.9f, height01));

                    Color splatSample = SampleSplatBilinear(splat, sr, origin, worldSize, wx, wz);
                    float path = Mathf.Clamp01(splatSample.r);
                    float rock = Mathf.Clamp01(splatSample.g);
                    c = Color.Lerp(c, rockColor, rock * 0.85f);
                    c = Color.Lerp(c, pathColor, path);

                    // Rivers / valleys below water surface (TerrainGenerator.baseHeight).
                    float depth = baseH - h;
                    if (depth > 0f)
                    {
                        float water = Mathf.Clamp01(depth / Mathf.Max(1e-3f, waterDepthBlend));
                        c = Color.Lerp(c, waterColor, water);
                    }

                    _pixels[y * res + x] = (Color32)c;
                }
            }

            PaintVillages(res, origin, viewSize);
            _mapTexture.SetPixels32(_pixels);
            _mapTexture.Apply(false, false);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
        }
    }

    float GetViewSize(float worldSize) => worldSize / Mathf.Max(1e-3f, zoom);

    void PaintVillages(int res, Vector3 origin, float viewSize)
    {
        float r = Mathf.Max(1.5f, villageMarkerRadiusTexels);
        float rSq = r * r;
        Color32 fill = villageColor;
        Color32 outline = new Color32(40, 28, 8, 255);

        // Prefer SettlementBuilder world centers (flat-ground snap + mesh layout origin).
        // Fall back to planned centers before any live instance has reported in.
        var settlements = SettlementService.Instance != null
            ? SettlementService.Instance.Settlements
            : null;
        if (settlements != null && settlements.Count > 0)
        {
            _lastPaintedSettlementCenters.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                Vector3 center = settlements[i].WorldCenter;
                _lastPaintedSettlementCenters.Add(center);
                PaintVillageMarker(center, res, origin, viewSize, r, rSq, fill, outline);
            }
            return;
        }

        _lastPaintedSettlementCenters.Clear();
        var coordinator = ResolveWorldGeneration();
        IReadOnlyList<Vector3> centers = coordinator != null
            ? coordinator.PlannedSettlementCenters
            : null;
        if (centers == null || centers.Count == 0)
            return;

        for (int i = 0; i < centers.Count; i++)
        {
            _lastPaintedSettlementCenters.Add(centers[i]);
            PaintVillageMarker(centers[i], res, origin, viewSize, r, rSq, fill, outline);
        }
    }

    void PaintVillageMarker(
        Vector3 worldPos,
        int res,
        Vector3 origin,
        float viewSize,
        float r,
        float rSq,
        Color32 fill,
        Color32 outline)
    {
        float u = (worldPos.x - origin.x) / viewSize + 0.5f;
        float v = (worldPos.z - origin.z) / viewSize + 0.5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return;

        float cx = u * res - 0.5f;
        float cy = v * res - 0.5f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1f));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt(cx + r + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1f));
        int y1 = Mathf.Min(res - 1, Mathf.CeilToInt(cy + r + 1f));

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - (cx + 0.5f);
                float dy = y + 0.5f - (cy + 0.5f);
                float dSq = dx * dx + dy * dy;
                if (dSq > rSq)
                    continue;
                _pixels[y * res + x] = dSq > (r - 1.1f) * (r - 1.1f) ? outline : fill;
            }
        }
    }

    void UpdatePlayerMarker()
    {
        if (_playerMarker == null || _mapRect == null)
            return;

        var gen = _boundTerrain != null ? _boundTerrain : ResolveTerrain();
        Transform player = PlayerReference.TryGetTransform();
        if (gen == null || !gen.IsTerrainReady || player == null)
        {
            _playerMarker.gameObject.SetActive(false);
            return;
        }

        Vector3 origin = gen.transform.position;
        float viewSize = GetViewSize(gen.worldSize);
        float u = (player.position.x - origin.x) / viewSize + 0.5f;
        float v = (player.position.z - origin.z) / viewSize + 0.5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            _playerMarker.gameObject.SetActive(false);
            return;
        }

        Vector2 size = _mapRect.rect.size;
        _playerMarker.anchoredPosition = new Vector2((u - 0.5f) * size.x, (v - 0.5f) * size.y);
        _playerMarker.gameObject.SetActive(true);
    }

    void UpdateQuestMarker()
    {
        if (_questMarker == null || _mapRect == null)
            return;

        var quests = QuestService.Instance;
        ActiveQuest q = quests != null ? quests.Active : null;
        if (q == null || q.Status != QuestStatus.Active)
        {
            _questMarker.gameObject.SetActive(false);
            return;
        }

        var gen = _boundTerrain != null ? _boundTerrain : ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
        {
            _questMarker.gameObject.SetActive(false);
            return;
        }

        Vector3 origin = gen.transform.position;
        float viewSize = GetViewSize(gen.worldSize);
        Vector3 target = q.TargetPosition;
        float u = (target.x - origin.x) / viewSize + 0.5f;
        float v = (target.z - origin.z) / viewSize + 0.5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            _questMarker.gameObject.SetActive(false);
            return;
        }

        Vector2 size = _mapRect.rect.size;
        _questMarker.anchoredPosition = new Vector2((u - 0.5f) * size.x, (v - 0.5f) * size.y);

        float pulse = 0.8f + 0.2f * Mathf.Sin(_questPulse * 4f);
        if (_questMarkerImage != null)
        {
            Color c = questTargetColor;
            c.a = pulse;
            _questMarkerImage.color = c;
        }

        float scale = 1f + 0.12f * Mathf.Sin(_questPulse * 4f);
        _questMarker.sizeDelta = new Vector2(questMarkerSizePixels * scale, questMarkerSizePixels * scale);
        _questMarker.gameObject.SetActive(true);
    }

    TerrainGenerator ResolveTerrain() =>
        terrainGenerator != null ? terrainGenerator : TerrainGenerator.GetActiveOrFind();

    WorldGenerationCoordinator ResolveWorldGeneration()
    {
        if (worldGeneration != null)
            return worldGeneration;
        return FindAnyObjectByType<WorldGenerationCoordinator>();
    }

    static float SampleHeightBilinear(
        NativeArray<float> heightmap,
        int hr,
        Vector3 origin,
        float worldSize,
        float worldX,
        float worldZ)
    {
        float fx = (worldX - origin.x + worldSize * 0.5f) / worldSize * (hr - 1);
        float fz = (worldZ - origin.z + worldSize * 0.5f) / worldSize * (hr - 1);
        int ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, hr - 2);
        int iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, hr - 2);
        float tx = fx - ix;
        float tz = fz - iz;
        float h00 = heightmap[iz * hr + ix];
        float h10 = heightmap[iz * hr + (ix + 1)];
        float h01 = heightmap[(iz + 1) * hr + ix];
        float h11 = heightmap[(iz + 1) * hr + (ix + 1)];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    static Color SampleSplatBilinear(
        NativeArray<Color> splat,
        int sr,
        Vector3 origin,
        float worldSize,
        float worldX,
        float worldZ)
    {
        float fx = ((worldX - origin.x) / worldSize + 0.5f) * sr - 0.5f;
        float fz = ((worldZ - origin.z) / worldSize + 0.5f) * sr - 0.5f;
        int ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, sr - 2);
        int iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, sr - 2);
        float tx = fx - ix;
        float tz = fz - iz;
        Color c00 = splat[iz * sr + ix];
        Color c10 = splat[iz * sr + (ix + 1)];
        Color c01 = splat[(iz + 1) * sr + ix];
        Color c11 = splat[(iz + 1) * sr + (ix + 1)];
        return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), tz);
    }
}
