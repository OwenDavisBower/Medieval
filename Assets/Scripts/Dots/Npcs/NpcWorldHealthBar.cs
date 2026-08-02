using UnityEngine;
using UnityEngine.UI;

namespace Medieval.Npcs
{
    /// <summary>
    /// Pooled world-space health bar for DOTS NPCs (layer HealthBar). Driven by
    /// <see cref="NpcHealthBarPresentationSystem"/> — not attached to entity GameObjects.
    /// </summary>
    public sealed class NpcWorldHealthBar : MonoBehaviour
    {
        const float HeightOffset = 2.15f;
        const float BarScale = 0.011f;
        const float MaxBillboardTiltDegrees = 15f;
        const string HealthBarLayerName = "HealthBar";

        RectTransform _fillRect;
        Image _fillImage;
        Text _levelLabel;
        Transform _billboardRoot;
        int _cachedLevel = int.MinValue;
        static Sprite s_whiteSprite;
        static Font s_font;
        static int s_billboardCamFrame = -1;
        static Camera s_billboardMainCam;

        public static NpcWorldHealthBar Create(Transform parent)
        {
            var go = new GameObject("NpcWorldHealthBar");
            if (parent != null)
                go.transform.SetParent(parent, false);

            var bar = go.AddComponent<NpcWorldHealthBar>();
            bar.Build();
            bar.SetVisible(false);
            return bar;
        }

        public void Sync(Vector3 feetPosition, float currentHealth, float maxHealth, int level)
        {
            transform.position = feetPosition + Vector3.up * HeightOffset;
            RefreshBarVisual(currentHealth, maxHealth);
            RefreshLevelLabel(level);

            bool show = currentHealth > 0.001f && currentHealth < maxHealth - 0.001f;
            SetVisible(show);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
            if (!visible)
                _cachedLevel = int.MinValue;
        }

        void LateUpdate()
        {
            if (_billboardRoot == null || !gameObject.activeInHierarchy)
                return;

            if (Time.frameCount != s_billboardCamFrame)
            {
                s_billboardCamFrame = Time.frameCount;
                s_billboardMainCam = Camera.main;
            }

            var cam = s_billboardMainCam;
            if (cam == null)
                return;

            Vector3 toCam = cam.transform.position - _billboardRoot.position;
            if (toCam.sqrMagnitude < 0.0001f)
                return;

            Quaternion relCam = cam.transform.rotation;
            Quaternion faceCam = Quaternion.LookRotation(-toCam.normalized, Vector3.up);

            Vector3 vp = cam.WorldToViewportPoint(_billboardRoot.position);
            float tiltScale = vp.z <= 0f ? 0f : Mathf.Clamp01(Mathf.Abs(vp.x - 0.5f) * 2f);
            float maxTilt = MaxBillboardTiltDegrees * tiltScale;
            _billboardRoot.rotation = Quaternion.RotateTowards(relCam, faceCam, maxTilt);
        }

        void RefreshBarVisual(float current, float maxHealth)
        {
            if (_fillRect == null)
                return;
            float t = maxHealth > 0.01f ? Mathf.Clamp01(current / maxHealth) : 0f;
            _fillRect.anchorMax = new Vector2(t, 1f);
            if (_fillImage != null)
                _fillImage.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.25f, 0.85f, 0.35f), t);
        }

        void RefreshLevelLabel(int level)
        {
            if (_levelLabel == null || level == _cachedLevel)
                return;
            _cachedLevel = level;
            _levelLabel.text = level > 0 ? $"Lv {level}" : string.Empty;
        }

        void Build()
        {
            _billboardRoot = transform;

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100f, 30f);
            canvasGo.transform.localScale = Vector3.one * BarScale;

            var levelGo = new GameObject("Level");
            levelGo.transform.SetParent(canvasGo.transform, false);
            _levelLabel = levelGo.AddComponent<Text>();
            _levelLabel.font = BuiltinFont();
            _levelLabel.fontSize = 18;
            _levelLabel.fontStyle = FontStyle.Bold;
            _levelLabel.alignment = TextAnchor.MiddleCenter;
            _levelLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _levelLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _levelLabel.color = new Color(1f, 0.92f, 0.35f, 1f);
            _levelLabel.raycastTarget = false;
            var levelRect = _levelLabel.GetComponent<RectTransform>();
            levelRect.anchorMin = new Vector2(0f, 0.45f);
            levelRect.anchorMax = Vector2.one;
            levelRect.offsetMin = Vector2.zero;
            levelRect.offsetMax = Vector2.zero;

            var barGo = new GameObject("Bar");
            barGo.transform.SetParent(canvasGo.transform, false);
            var barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = new Vector2(1f, 0.4f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(barGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.sprite = WhiteSprite();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.92f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(barGo.transform, false);
            _fillImage = fillGo.AddComponent<Image>();
            _fillImage.sprite = WhiteSprite();
            _fillImage.color = new Color(0.25f, 0.85f, 0.35f, 1f);
            _fillRect = _fillImage.GetComponent<RectTransform>();
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = Vector2.one;
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;

            int hb = LayerMask.NameToLayer(HealthBarLayerName);
            if (hb >= 0)
                HierarchyLayers.SetRecursive(transform, hb);
        }

        static Sprite WhiteSprite()
        {
            if (s_whiteSprite != null)
                return s_whiteSprite;
            var tex = Texture2D.whiteTexture;
            s_whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return s_whiteSprite;
        }

        static Font BuiltinFont()
        {
            if (s_font != null)
                return s_font;
            s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (s_font == null)
                s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return s_font;
        }
    }
}
