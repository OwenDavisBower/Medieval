#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        var gen = (TerrainGenerator)target;
        if (GUILayout.Button("Generate Terrain", GUILayout.Height(28)))
        {
            Undo.RecordObject(gen, "Generate Terrain");
            gen.GenerateTerrain();
        }
    }
}
#endif
