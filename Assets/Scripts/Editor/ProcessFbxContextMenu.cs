using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Extracts embedded textures/materials beside the FBX, assigns <see cref="StylizedToonLitShaderAssetPath"/>,
/// trims unused textures, lowers base map max size, decimates via Blender, then remaps the decimated FBX to the material.
/// </summary>
public static class ProcessFbxContextMenu
{
    const string StylizedToonLitShaderAssetPath = "Assets/Shaders/StylizedToonLit.shader";

    static readonly string[] BaseTexturePropertyFallbacks =
    {
        "_BaseMap",
        "_MainTex",
        "baseMap",
        "_Albedo",
        "_Diffuse",
        "_ColorMap"
    };

    [MenuItem("Assets/ProcessFBX/Process 200", false, 1205)]
    static void Process200() => RunProcess(200);

    [MenuItem("Assets/ProcessFBX/Process 200", true)]
    static bool Validate200() => HasFbxSelection();

    [MenuItem("Assets/ProcessFBX/Process 500", false, 1206)]
    static void Process500() => RunProcess(500);

    [MenuItem("Assets/ProcessFBX/Process 500", true)]
    static bool Validate500() => HasFbxSelection();

    [MenuItem("Assets/ProcessFBX/Process 1000", false, 1207)]
    static void Process1000() => RunProcess(1000);

    [MenuItem("Assets/ProcessFBX/Process 1000", true)]
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

    static void RunProcess(int targetFaceCount)
    {
        var assetPaths = GetSelectedFbxPaths().Distinct().ToList();
        if (assetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("ProcessFBX", "Select one or more FBX assets under Assets.", "OK");
            return;
        }

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(StylizedToonLitShaderAssetPath);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "ProcessFBX",
                $"Could not load shader at:\n{StylizedToonLitShaderAssetPath}",
                "OK");
            return;
        }

        var scriptPath = FbxBlenderDecimateUtil.GetDecimateScriptPath();
        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog("ProcessFBX", $"Decimate script not found:\n{scriptPath}", "OK");
            return;
        }

        if (!FbxBlenderDecimateUtil.TryGetBlenderExecutable(out var blender, out var resolveError))
        {
            EditorUtility.DisplayDialog("ProcessFBX", resolveError, "OK");
            return;
        }

        var failed = new List<string>();
        try
        {
            for (var i = 0; i < assetPaths.Count; i++)
            {
                var assetPath = assetPaths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "ProcessFBX",
                        $"{assetPath} (extract → toon → {targetFaceCount} faces)",
                        (float)i / Mathf.Max(1, assetPaths.Count)))
                {
                    EditorUtility.DisplayDialog("ProcessFBX", "Cancelled.", "OK");
                    return;
                }

                if (!TryProcessOne(assetPath, shader, blender, scriptPath, targetFaceCount, out var err))
                    failed.Add($"{assetPath}\n{err}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (failed.Count > 0)
        {
            var msg = $"{failed.Count} of {assetPaths.Count} failed:\n\n" + string.Join("\n\n", failed);
            Debug.LogError(msg);
            EditorUtility.DisplayDialog("ProcessFBX", msg, "OK");
        }
        else
        {
            Debug.Log(
                $"ProcessFBX ({targetFaceCount}): completed {assetPaths.Count} file(s). " +
                "Decimated mesh is <name>_<count>poly.fbx beside the source.");
        }
    }

    static bool TryProcessOne(
        string fbxPath,
        Shader targetShader,
        string blenderExe,
        string scriptPath,
        int targetFaceCount,
        out string error)
    {
        error = null;
        fbxPath = fbxPath.Replace('\\', '/');

        var folder = Path.GetDirectoryName(fbxPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
        {
            error = "Could not resolve FBX folder.";
            return false;
        }

        var fbxStem = SanitizeFileName(Path.GetFileNameWithoutExtension(fbxPath));

        // Same path as the Model inspector "Extract Textures" — updates material refs and file formats.
        var modelImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (modelImporter == null)
        {
            error = "Could not get ModelImporter for FBX.";
            return false;
        }

        var texturePathsBefore = CollectTextureAssetPathsUnderFolder(folder);
        modelImporter.ExtractTextures(folder);
        AssetDatabase.Refresh();
        var texturePathsAfter = CollectTextureAssetPathsUnderFolder(folder);
        var extractedTexturePaths = texturePathsAfter
            .Where(p => !texturePathsBefore.Contains(p))
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToList();

        var extractedMaterialPaths = new List<string>();
        var subMaterials = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<Material>()
            .Where(AssetDatabase.IsSubAsset)
            .ToList();

        foreach (var mat in subMaterials)
        {
            var dest = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fbxStem}Material.mat");
            var extractErr = AssetDatabase.ExtractAsset(mat, dest);
            if (!string.IsNullOrEmpty(extractErr))
            {
                error = $"Failed to extract material '{mat.name}': {extractErr}";
                return false;
            }

            extractedMaterialPaths.Add(dest.Replace('\\', '/'));
        }

        if (extractedMaterialPaths.Count == 0)
        {
            error = "No embedded materials to extract; nothing to assign after decimate.";
            return false;
        }

        AssetDatabase.Refresh();

        var primaryMaterialPath = extractedMaterialPaths.OrderBy(p => p, System.StringComparer.Ordinal).First();
        var primaryMaterial = AssetDatabase.LoadAssetAtPath<Material>(primaryMaterialPath);
        if (primaryMaterial == null)
        {
            error = $"Could not load material at {primaryMaterialPath}.";
            return false;
        }

        var keepTexturePaths = new HashSet<string>(System.StringComparer.Ordinal);
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var matPath in extractedMaterialPaths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    continue;

                var baseTex = FindBaseColorTexture(mat);
                Undo.RecordObject(mat, "Process FBX material");
                mat.shader = targetShader;
                if (baseTex != null && mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", baseTex);

                EditorUtility.SetDirty(mat);

                if (baseTex != null)
                {
                    var tp = AssetDatabase.GetAssetPath(baseTex).Replace('\\', '/');
                    if (!string.IsNullOrEmpty(tp))
                        keepTexturePaths.Add(tp);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        foreach (var texPath in extractedTexturePaths)
        {
            if (keepTexturePaths.Contains(texPath))
                continue;
            AssetDatabase.MoveAssetToTrash(texPath);
        }

        AssetDatabase.Refresh();

        keepTexturePaths = RenameKeptTexturesToFbxStem(folder, fbxStem, keepTexturePaths);
        AssetDatabase.Refresh();

        foreach (var tp in keepTexturePaths)
            ApplyBaseMapImportSettings(tp);

        if (!FbxBlenderDecimateUtil.RunDecimateOnce(blenderExe, scriptPath, fbxPath, targetFaceCount, out var blenderErr))
        {
            error = blenderErr;
            return false;
        }

        AssetDatabase.Refresh();

        var stem = Path.GetFileNameWithoutExtension(fbxPath);
        var fbxExt = Path.GetExtension(fbxPath);
        var decimatedAssetPath = $"{folder}/{stem}_{targetFaceCount}poly{fbxExt}".Replace('\\', '/');

        if (!File.Exists(FbxBlenderDecimateUtil.GetFullAssetPath(decimatedAssetPath)))
        {
            error = $"Expected decimated FBX not found:\n{decimatedAssetPath}";
            return false;
        }

        var extractedMaterialsOrdered = extractedMaterialPaths
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(p => AssetDatabase.LoadAssetAtPath<Material>(p))
            .Where(m => m != null)
            .ToList();

        RemapEmbeddedMaterials(decimatedAssetPath, extractedMaterialsOrdered, primaryMaterial);
        return true;
    }

    static void RemapEmbeddedMaterials(
        string fbxAssetPath,
        List<Material> extractedMaterialsOrdered,
        Material fallbackMaterial)
    {
        var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
        if (importer == null)
            return;

        var embedded = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath)
            .OfType<Material>()
            .Where(AssetDatabase.IsSubAsset)
            .ToList();

        foreach (var em in embedded)
        {
            var match = extractedMaterialsOrdered.FirstOrDefault(m => m.name == em.name) ?? fallbackMaterial;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(em), match);
        }

        EditorUtility.SetDirty(importer);
        AssetDatabase.WriteImportSettingsIfDirty(fbxAssetPath);
        AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceUpdate);
    }

    static void ApplyBaseMapImportSettings(string textureAssetPath)
    {
        var ti = AssetImporter.GetAtPath(textureAssetPath) as TextureImporter;
        if (ti == null)
            return;

        ti.maxTextureSize = 256;
        EditorUtility.SetDirty(ti);
        ti.SaveAndReimport();
    }

    static Texture2D FindBaseColorTexture(Material mat)
    {
        var shader = mat.shader;
        if (shader != null)
        {
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                    continue;
                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.MainTexture) == 0)
                    continue;
                var propName = shader.GetPropertyName(i);
                return mat.GetTexture(propName) as Texture2D;
            }
        }

        foreach (var name in BaseTexturePropertyFallbacks)
        {
            if (!mat.HasProperty(name))
                continue;
            var t = mat.GetTexture(name) as Texture2D;
            if (t != null)
                return t;
        }

        return null;
    }

    static HashSet<string> CollectTextureAssetPathsUnderFolder(string folder)
    {
        folder = folder.Replace('\\', '/').TrimEnd('/');
        var set = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (AssetPathIsUnderFolder(p, folder))
                set.Add(p);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Cubemap", new[] { folder }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (AssetPathIsUnderFolder(p, folder))
                set.Add(p);
        }

        return set;
    }

    /// <summary>
    /// Renames kept textures to <c>{fbxStem}Texture</c> (+ extension), e.g. House.fbx → HouseTexture.png.
    /// Additional kept textures use <c>{fbxStem}Texture_2</c>, etc., with <see cref="AssetDatabase.GenerateUniqueAssetPath"/> for collisions.
    /// </summary>
    static HashSet<string> RenameKeptTexturesToFbxStem(
        string folder,
        string fbxStem,
        HashSet<string> keepTexturePaths)
    {
        if (keepTexturePaths.Count == 0)
            return keepTexturePaths;

        folder = folder.Replace('\\', '/').TrimEnd('/');
        var ordered = keepTexturePaths.OrderBy(p => p, System.StringComparer.Ordinal).ToList();
        var result = new HashSet<string>(System.StringComparer.Ordinal);

        for (var i = 0; i < ordered.Count; i++)
        {
            var tp = ordered[i].Replace('\\', '/');
            var ext = Path.GetExtension(tp);
            var nameStem = i == 0 ? $"{fbxStem}Texture" : $"{fbxStem}Texture_{i + 1}";
            var dest = $"{folder}/{SanitizeFileName(nameStem)}{ext}".Replace('\\', '/');
            dest = AssetDatabase.GenerateUniqueAssetPath(dest).Replace('\\', '/');

            if (!string.Equals(tp, dest, System.StringComparison.OrdinalIgnoreCase))
            {
                var err = AssetDatabase.MoveAsset(tp, dest);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogWarning($"ProcessFBX: could not rename texture '{tp}' → '{dest}': {err}");
                    result.Add(tp);
                    continue;
                }
            }

            result.Add(dest);
        }

        return result;
    }

    static bool AssetPathIsUnderFolder(string assetPath, string folder)
    {
        assetPath = assetPath.Replace('\\', '/');
        folder = folder.Replace('\\', '/').TrimEnd('/');
        return string.Equals(assetPath, folder, System.StringComparison.Ordinal)
            || assetPath.StartsWith(folder + "/", System.StringComparison.Ordinal);
    }

    static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Asset";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Asset" : name;
    }
}
