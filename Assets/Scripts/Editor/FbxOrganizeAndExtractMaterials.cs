using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Moves a selected FBX into a folder named after the file, extracts embedded materials beside it,
/// and assigns <see cref="StylizedToonLitShaderAssetPath"/>.
/// </summary>
public static class FbxOrganizeAndExtractMaterials
{
    const string StylizedToonLitShaderAssetPath = "Assets/Shaders/StylizedToonLit.shader";

    [MenuItem("Assets/Organize FBX and Extract Materials", false, 1200)]
    static void OrganizeFromMenu()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(StylizedToonLitShaderAssetPath);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Organize FBX",
                $"Could not load shader at:\n{StylizedToonLitShaderAssetPath}",
                "OK");
            return;
        }

        var fbxPaths = GetSelectedFbxPaths().Distinct().ToList();
        if (fbxPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Organize FBX", "Select one or more FBX assets under Assets.", "OK");
            return;
        }

        int ok = 0;
        foreach (var path in fbxPaths)
        {
            if (TryProcessFbx(path, shader, out var error))
                ok++;
            else if (!string.IsNullOrEmpty(error))
                Debug.LogWarning($"Organize FBX skipped '{path}': {error}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Organize FBX: processed {ok} / {fbxPaths.Count} file(s).");
    }

    [MenuItem("Assets/Organize FBX and Extract Materials", true)]
    static bool OrganizeFromMenuValidate()
    {
        return GetSelectedFbxPaths().Any();
    }

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

    static bool TryProcessFbx(string fbxPath, Shader targetShader, out string error)
    {
        error = null;
        fbxPath = fbxPath.Replace('\\', '/');

        var fileName = Path.GetFileName(fbxPath);
        var baseName = Path.GetFileNameWithoutExtension(fbxPath);
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(baseName))
        {
            error = "Invalid file name.";
            return false;
        }

        var parentDir = Path.GetDirectoryName(fbxPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parentDir))
        {
            error = "Could not resolve parent folder.";
            return false;
        }

        var parentFolderName = Path.GetFileName(parentDir);
        var targetFolder = string.Equals(parentFolderName, baseName, System.StringComparison.Ordinal)
            ? parentDir
            : $"{parentDir}/{baseName}";
        var targetFbxPath = $"{targetFolder}/{fileName}";

        if (fbxPath != targetFbxPath)
        {
            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                var folderGuid = AssetDatabase.CreateFolder(parentDir, baseName);
                if (string.IsNullOrEmpty(folderGuid))
                {
                    error = $"Failed to create folder '{targetFolder}'.";
                    return false;
                }
            }

            var moveError = AssetDatabase.MoveAsset(fbxPath, targetFbxPath);
            if (!string.IsNullOrEmpty(moveError))
            {
                error = moveError;
                return false;
            }

            fbxPath = targetFbxPath;
        }

        var extractedMaterialPaths = new List<string>();
        var embeddedMaterials = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<Material>()
            .Where(AssetDatabase.IsSubAsset)
            .ToList();

        foreach (var mat in embeddedMaterials)
        {
            var dest = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{SanitizeFileName(mat.name)}.mat");
            var extractErr = AssetDatabase.ExtractAsset(mat, dest);
            if (!string.IsNullOrEmpty(extractErr))
            {
                error = $"Failed to extract material '{mat.name}': {extractErr}";
                return false;
            }

            extractedMaterialPaths.Add(dest.Replace('\\', '/'));
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var matPath in extractedMaterialPaths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    continue;

                Undo.RecordObject(mat, "Set FBX material shader");
                mat.shader = targetShader;
                EditorUtility.SetDirty(mat);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        return true;
    }

    static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Material";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Material" : name;
    }
}
