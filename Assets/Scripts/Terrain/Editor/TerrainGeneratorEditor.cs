using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
public sealed class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Splat map preview", EditorStyles.boldLabel);
        var gen = (TerrainGenerator)target;
        var splat = gen.SplatmapTexture;
        if (splat == null)
        {
            EditorGUILayout.HelpBox("Generate terrain to build the splat map preview.", MessageType.Info);
        }
        else
        {
            float maxWidth = EditorGUIUtility.currentViewWidth - 40f;
            float aspect = (float)splat.width / Mathf.Max(1, splat.height);
            float previewHeight = maxWidth / aspect;
            Rect rect = GUILayoutUtility.GetRect(maxWidth, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, splat);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate"))
        {
            Undo.RecordObject(gen, "Generate Terrain");
            gen.Regenerate(notifyDependents: true);
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }

        if (GUILayout.Button(new GUIContent(
                "Re-generate",
                "Rebuild heightmap, splat, and chunk meshes. Does not invoke TerrainGenerated (settlements, trees, and other listeners are not re-run). NavMesh still rebuilds to match terrain.")))
        {
            Undo.RecordObject(gen, "Re-generate Terrain");
            gen.Regenerate(notifyDependents: false);
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "While playing, use Re-generate to iterate terrain. Generate also fires TerrainGenerated and may re-run world spawners.",
                MessageType.Info);
        }
    }
}
