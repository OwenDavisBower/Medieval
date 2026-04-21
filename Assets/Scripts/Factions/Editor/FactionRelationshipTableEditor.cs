using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FactionRelationshipTable))]
public sealed class FactionRelationshipTableEditor : Editor
{
    SerializedProperty _defaultBetweenUnlisted;
    SerializedProperty _matrixCapacityHint;
    SerializedProperty _explicitPairs;

    Vector2 _scrollPos;
    const float CellMinWidth = 72f;
    const float RowHeaderWidth = 120f;

    void OnEnable()
    {
        _defaultBetweenUnlisted = serializedObject.FindProperty("defaultBetweenUnlisted");
        _matrixCapacityHint = serializedObject.FindProperty("matrixCapacityHint");
        _explicitPairs = serializedObject.FindProperty("explicitPairs");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_defaultBetweenUnlisted);
        EditorGUILayout.PropertyField(_matrixCapacityHint);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Relationship matrix", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Rows/columns are all FactionDefinition assets in the project. Diagonal is always Allied. " +
            "Off-diagonal cells edit the symmetric pair; matching Default clears the explicit entry.",
            MessageType.None);

        List<FactionDefinition> factions = LoadAllFactionDefinitions();
        if (factions.Count == 0)
        {
            EditorGUILayout.HelpBox("No FactionDefinition assets found.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        WarnDuplicateFactionIds(factions);

        var table = (FactionRelationshipTable)target;
        Relationship defaultRel = (Relationship)_defaultBetweenUnlisted.enumValueIndex;

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        DrawGrid(table, factions, defaultRel);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(_explicitPairs, true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    static void WarnDuplicateFactionIds(List<FactionDefinition> factions)
    {
        var seen = new HashSet<int>();
        var duplicateIds = new HashSet<int>();
        for (int i = 0; i < factions.Count; i++)
        {
            int id = factions[i].FactionID;
            if (!seen.Add(id))
                duplicateIds.Add(id);
        }

        if (duplicateIds.Count > 0)
        {
            var list = new List<int>(duplicateIds);
            list.Sort();
            EditorGUILayout.HelpBox(
                $"Duplicate FactionID(s): {string.Join(", ", list)} — resolve in faction assets; matrix uses first row per ID in sort order.",
                MessageType.Warning);
        }
    }

    void DrawGrid(FactionRelationshipTable table, List<FactionDefinition> factions, Relationship defaultRel)
    {
        int n = factions.Count;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(RowHeaderWidth);
        for (int c = 0; c < n; c++)
            GUILayout.Label(TruncateLabel(factions[c]), EditorStyles.miniLabel, GUILayout.Width(CellMinWidth), GUILayout.MaxHeight(36));
        EditorGUILayout.EndHorizontal();

        for (int row = 0; row < n; row++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(TruncateLabel(factions[row]), EditorStyles.miniLabel, GUILayout.Width(RowHeaderWidth), GUILayout.Height(20));

            for (int col = 0; col < n; col++)
            {
                if (col == row)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.EnumPopup(Relationship.Allied, GUILayout.Width(CellMinWidth));
                    }
                }
                else
                {
                    FactionDefinition a = factions[row];
                    FactionDefinition b = factions[col];
                    Relationship current = GetPairRelationship(table, a, b, defaultRel);

                    EditorGUI.BeginChangeCheck();
                    Relationship next = (Relationship)EditorGUILayout.EnumPopup(current, GUILayout.Width(CellMinWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(table, "Set Faction Relationship");
                        serializedObject.Update();
                        SetPairSerialized(a, b, next, defaultRel);
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(table);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    static string TruncateLabel(FactionDefinition d)
    {
        string s = $"{d.FactionName} ({d.FactionID})";
        if (s.Length > 22)
            return s.Substring(0, 19) + "…";
        return s;
    }

    static Relationship GetPairRelationship(FactionRelationshipTable table, FactionDefinition a, FactionDefinition b, Relationship defaultRel)
    {
        IReadOnlyList<FactionRelationshipEntry> pairs = table.ExplicitPairs;
        for (int i = 0; i < pairs.Count; i++)
        {
            FactionRelationshipEntry e = pairs[i];
            if (e.FactionA == null || e.FactionB == null)
                continue;
            if ((e.FactionA == a && e.FactionB == b) || (e.FactionA == b && e.FactionB == a))
                return e.Relationship;
        }

        return defaultRel;
    }

    void SetPairSerialized(FactionDefinition a, FactionDefinition b, Relationship value, Relationship defaultRel)
    {
        RemovePairEntries(a, b);

        if (value != defaultRel)
        {
            int idx = _explicitPairs.arraySize;
            _explicitPairs.arraySize++;
            SerializedProperty el = _explicitPairs.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("FactionA").objectReferenceValue = a;
            el.FindPropertyRelative("FactionB").objectReferenceValue = b;
            el.FindPropertyRelative("Relationship").enumValueIndex = (int)value;
        }
    }

    void RemovePairEntries(FactionDefinition a, FactionDefinition b)
    {
        for (int i = _explicitPairs.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty el = _explicitPairs.GetArrayElementAtIndex(i);
            var fa = el.FindPropertyRelative("FactionA").objectReferenceValue as FactionDefinition;
            var fb = el.FindPropertyRelative("FactionB").objectReferenceValue as FactionDefinition;
            if (fa == null || fb == null)
                continue;
            if ((fa == a && fb == b) || (fa == b && fb == a))
                _explicitPairs.DeleteArrayElementAtIndex(i);
        }
    }

    static List<FactionDefinition> LoadAllFactionDefinitions()
    {
        string[] guids = AssetDatabase.FindAssets("t:FactionDefinition");
        var list = new List<FactionDefinition>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var def = AssetDatabase.LoadAssetAtPath<FactionDefinition>(path);
            if (def != null)
                list.Add(def);
        }

        list.Sort((x, y) => x.FactionID.CompareTo(y.FactionID));
        return list;
    }
}
