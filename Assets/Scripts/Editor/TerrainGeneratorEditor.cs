using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    const string MenuPath = "Medieval/Terrain/Regenerate Selected Terrain";

    [MenuItem(MenuPath)]
    static void RegenerateFromMenu()
    {
        var gen = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<TerrainGenerator>()
            : null;
        if (gen == null)
        {
            EditorUtility.DisplayDialog(
                "Regenerate Terrain",
                "Select a GameObject with a TerrainGenerator component.",
                "OK");
            return;
        }

        var terrain = gen.GetComponent<Terrain>();
        var data = terrain.terrainData;
        Undo.RecordObject(data, "Regenerate Terrain");
        Undo.RecordObject(terrain, "Regenerate Terrain");
        gen.RegenerateTerrain(placePlayerAndFireEvent: false);
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(gen);
    }

    [MenuItem(MenuPath, validate = true)]
    static bool RegenerateFromMenuValidate()
    {
        return Selection.activeGameObject != null
            && Selection.activeGameObject.GetComponent<TerrainGenerator>() != null;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (TerrainGenerator)target;
        var terrain = gen.GetComponent<Terrain>();
        var data = terrain != null ? terrain.terrainData : null;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(data == null))
        {
            if (GUILayout.Button("Regenerate Terrain"))
            {
                Undo.RecordObject(data, "Regenerate Terrain");
                Undo.RecordObject(terrain, "Regenerate Terrain");
                gen.RegenerateTerrain(placePlayerAndFireEvent: false);
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(terrain);
                EditorUtility.SetDirty(gen);
            }
        }
    }
}
