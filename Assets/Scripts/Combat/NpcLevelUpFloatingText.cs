using UnityEngine;
using UnityEngine.UI;

/// <summary>Short-lived billboard label spawned above an NPC when they level up.</summary>
public sealed class NpcLevelUpFloatingText : MonoBehaviour
{
    const float DefaultLifetime = 1.65f;
    const float RiseSpeed = 1.1f;
    const float StartHeightOffset = -0.55f;
    const float WorldScale = 0.014f;
    const float StartScale = 0.28f;
    const float GrowDuration = 0.4f;
    const string HealthBarLayerName = "HealthBar";

    float _life;
    float _age;
    Text _label;
    Color _color;
    Transform _cam;
    static Font s_font;

    public static void Spawn(Vector3 worldPosition, int levelsGained = 1)
    {
        var go = new GameObject("LevelUpText");
        go.transform.position = worldPosition + Vector3.up * StartHeightOffset;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 250;

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 48f);
        go.transform.localScale = Vector3.one * (WorldScale * StartScale);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.text = levelsGained > 1 ? $"Level Up x{levelsGained}!" : "Level Up!";
        label.font = BuiltinFont();
        label.fontSize = 42;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = new Color(1f, 0.92f, 0.35f, 1f);
        label.raycastTarget = false;

        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var fx = go.AddComponent<NpcLevelUpFloatingText>();
        fx._label = label;
        fx._color = label.color;
        fx._life = DefaultLifetime;
        fx._age = 0f;

        int hb = LayerMask.NameToLayer(HealthBarLayerName);
        if (hb >= 0)
            HierarchyLayers.SetRecursive(go.transform, hb);
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

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        _age += dt;
        transform.position += Vector3.up * (RiseSpeed * dt);

        if (_cam == null)
        {
            var main = Camera.main;
            if (main != null)
                _cam = main.transform;
        }

        if (_cam != null)
        {
            Vector3 toCam = _cam.position - transform.position;
            if (toCam.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        float t = Mathf.Clamp01(_age / _life);
        float fade = t < 0.55f ? 1f : 1f - ((t - 0.55f) / 0.45f);
        if (_label != null)
        {
            Color c = _color;
            c.a = fade;
            _label.color = c;
        }

        float growT = Mathf.Clamp01(_age / GrowDuration);
        float growEase = 1f - (1f - growT) * (1f - growT);
        float scale = Mathf.Lerp(StartScale, 1f, growEase);
        transform.localScale = Vector3.one * (WorldScale * scale);

        if (_age >= _life)
            Destroy(gameObject);
    }
}
