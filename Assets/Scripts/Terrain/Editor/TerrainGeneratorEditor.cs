using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
public sealed class TerrainGeneratorEditor : Editor
{
    static readonly FieldInfo SplatmapRgbaField =
        typeof(TerrainGenerator).GetField("_splatmapRgba", BindingFlags.NonPublic | BindingFlags.Instance);

    Texture2D? _splatPreviewTexture;
    Color32[]? _previewPixels;
    int _previewRes;

    void OnDisable()
    {
        if (_splatPreviewTexture != null)
        {
            DestroyImmediate(_splatPreviewTexture);
            _splatPreviewTexture = null;
        }

        _previewPixels = null;
        _previewRes = 0;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Splat map preview", EditorStyles.boldLabel);
        var gen = (TerrainGenerator)target;

        if (!TryGetSplatFloatBuffer(gen, out var rgba, out var res))
        {
            EditorGUILayout.HelpBox(
                "Generate terrain to build the splat map preview (path mask floats).",
                MessageType.Info);
        }
        else
        {
            EnsurePreviewTexture(rgba, res);
            if (_splatPreviewTexture != null)
            {
                float maxWidth = EditorGUIUtility.currentViewWidth - 40f;
                float aspect = (float)_splatPreviewTexture.width / Mathf.Max(1, _splatPreviewTexture.height);
                float previewHeight = maxWidth / aspect;
                Rect rect = GUILayoutUtility.GetRect(maxWidth, previewHeight, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(rect, _splatPreviewTexture);
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate"))
        {
            Undo.RecordObject(gen, "Generate Terrain");
            gen.Regenerate();
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }
    }

    static bool TryGetSplatFloatBuffer(TerrainGenerator gen, out NativeArray<float> rgba, out int resolution)
    {
        rgba = default;
        resolution = 0;

        if (SplatmapRgbaField == null)
            return false;

        var value = SplatmapRgbaField.GetValue(gen);
        if (value == null)
            return false;

        rgba = (NativeArray<float>)value;
        if (!rgba.IsCreated || rgba.Length < 4 || rgba.Length % 4 != 0)
            return false;

        var texels = rgba.Length / 4;
        var r = Mathf.RoundToInt(Mathf.Sqrt(texels));
        if (r * r != texels)
            return false;

        resolution = r;
        return true;
    }

    void EnsurePreviewTexture(NativeArray<float> rgba, int res)
    {
        int n = res * res;
        if (_previewPixels == null || _previewRes != res)
        {
            _previewPixels = new Color32[n];
            _previewRes = res;
        }

        for (var i = 0; i < n; i++)
        {
            var b = i * 4;
            var rf = rgba[b];
            var gf = rgba[b + 1];
            var bf = rgba[b + 2];
            var af = rgba[b + 3];
            _previewPixels[i] = new Color32(
                Float01ToByte(rf),
                Float01ToByte(gf),
                Float01ToByte(bf),
                Float01ToByte(af));
        }

        if (_splatPreviewTexture != null &&
            (_splatPreviewTexture.width != res || _splatPreviewTexture.height != res))
        {
            DestroyImmediate(_splatPreviewTexture);
            _splatPreviewTexture = null;
        }

        if (_splatPreviewTexture == null)
        {
            _splatPreviewTexture = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
            {
                name = "SplatmapPreview",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        _splatPreviewTexture.SetPixels32(_previewPixels);
        _splatPreviewTexture.Apply(false, false);
    }

    static byte Float01ToByte(float v)
    {
        if (v <= 0f)
            return 0;
        if (v >= 1f)
            return 255;
        return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
