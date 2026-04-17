using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
public sealed class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        var gen = (TerrainGenerator)target;
        if (GUILayout.Button("Generate"))
        {
            Undo.RecordObject(gen, "Generate Terrain");
            gen.Regenerate();
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }
    }
}
