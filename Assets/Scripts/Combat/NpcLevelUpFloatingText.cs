using UnityEngine;

/// <summary>Short-lived billboard label spawned above an NPC when they level up.</summary>
public sealed class NpcLevelUpFloatingText : MonoBehaviour
{
    const float DefaultLifetime = 1.65f;
    const float RiseSpeed = 0.85f;
    const float StartScale = 0.12f;

    float _life;
    float _age;
    TextMesh _text;
    Color _color;
    Transform _cam;

    public static void Spawn(Vector3 worldPosition, int levelsGained = 1)
    {
        var go = new GameObject("LevelUpText");
        go.transform.position = worldPosition;

        var tm = go.AddComponent<TextMesh>();
        tm.text = levelsGained > 1 ? $"Level Up x{levelsGained}!" : "Level Up!";
        tm.fontSize = 48;
        tm.characterSize = 0.065f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.92f, 0.35f, 1f);
        tm.fontStyle = FontStyle.Bold;

        // Prefer built-in font so TMP is not required.
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (tm.font == null)
            tm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var fx = go.AddComponent<NpcLevelUpFloatingText>();
        fx._text = tm;
        fx._color = tm.color;
        fx._life = DefaultLifetime;
        fx._age = 0f;
        go.transform.localScale = Vector3.one * StartScale;

        int hb = LayerMask.NameToLayer("HealthBar");
        if (hb >= 0)
            go.layer = hb;
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
        if (_text != null)
        {
            Color c = _color;
            c.a = fade;
            _text.color = c;
        }

        float pop = 1f + 0.18f * Mathf.Sin(Mathf.Clamp01(t * 3.2f) * Mathf.PI);
        transform.localScale = Vector3.one * (StartScale * pop);

        if (_age >= _life)
            Destroy(gameObject);
    }
}
