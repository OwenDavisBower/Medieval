using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <c>UtilScripts/decimate_fbx_mesh.py</c> via Blender on selected FBX assets.
/// Requires Blender 3.x+ (same as the Python script).
/// </summary>
public static class DecimateFbxContextMenu
{
    [MenuItem("Assets/Decimate FBX/Decimate 200", false, 1210)]
    static void Decimate200() => RunDecimate(200);

    [MenuItem("Assets/Decimate FBX/Decimate 200", true)]
    static bool Validate200() => HasFbxSelection();

    [MenuItem("Assets/Decimate FBX/Decimate 500", false, 1211)]
    static void Decimate500() => RunDecimate(500);

    [MenuItem("Assets/Decimate FBX/Decimate 500", true)]
    static bool Validate500() => HasFbxSelection();

    [MenuItem("Assets/Decimate FBX/Decimate 1000", false, 1212)]
    static void Decimate1000() => RunDecimate(1000);

    [MenuItem("Assets/Decimate FBX/Decimate 1000", true)]
    static bool Validate1000() => HasFbxSelection();

    static bool HasFbxSelection() => GetSelectedFbxPaths().Any();

    static IEnumerable<string> GetSelectedFbxPaths()
    {
        foreach (var obj in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
                continue;

            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                continue;

            yield return path;
        }
    }

    static void RunDecimate(int targetFaceCount)
    {
        var assetPaths = GetSelectedFbxPaths().Distinct().ToList();
        if (assetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Decimate FBX", "Select one or more FBX assets under Assets.", "OK");
            return;
        }

        var scriptPath = FbxBlenderDecimateUtil.GetDecimateScriptPath();
        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog(
                "Decimate FBX",
                $"Script not found:\n{scriptPath}",
                "OK");
            return;
        }

        if (!FbxBlenderDecimateUtil.TryGetBlenderExecutable(out var blender, out var resolveError))
        {
            EditorUtility.DisplayDialog("Decimate FBX", resolveError, "OK");
            return;
        }

        var failed = new List<string>();
        try
        {
            for (var i = 0; i < assetPaths.Count; i++)
            {
                var assetPath = assetPaths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Decimate FBX",
                        $"{assetPath} → ~{targetFaceCount} faces",
                        (float)i / Mathf.Max(1, assetPaths.Count)))
                {
                    EditorUtility.DisplayDialog("Decimate FBX", "Cancelled.", "OK");
                    return;
                }

                if (!FbxBlenderDecimateUtil.RunDecimateOnce(blender, scriptPath, assetPath, targetFaceCount, out var err))
                    failed.Add($"{assetPath}\n{err}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();

        if (failed.Count > 0)
        {
            var msg = $"{failed.Count} of {assetPaths.Count} failed:\n\n" + string.Join("\n\n", failed);
            Debug.LogError(msg);
            EditorUtility.DisplayDialog("Decimate FBX", msg, "OK");
        }
        else
        {
            Debug.Log(
                $"Decimate FBX ({targetFaceCount}): completed {assetPaths.Count} file(s). " +
                "Outputs are <name>_<count>poly.fbx beside the source (see decimate_fbx_mesh.py).");
        }
    }
}
