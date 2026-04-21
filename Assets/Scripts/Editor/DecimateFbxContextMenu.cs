using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <c>UtilScripts/decimate_fbx_mesh.py</c> via Blender on selected FBX assets.
/// Requires Blender 3.x+ (same as the Python script).
/// </summary>
public static class DecimateFbxContextMenu
{
    const string BlenderEditorPrefsKey = "Medieval.BlenderExecutable";
    const string DecimateScriptRelative = "UtilScripts/decimate_fbx_mesh.py";

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

        var scriptPath = GetDecimateScriptPath();
        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog(
                "Decimate FBX",
                $"Script not found:\n{scriptPath}",
                "OK");
            return;
        }

        if (!TryGetBlenderExecutable(out var blender, out var resolveError))
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

                if (!RunBlenderOnce(blender, scriptPath, assetPath, targetFaceCount, out var err))
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

    static string GetProjectRoot() => Directory.GetParent(Application.dataPath)!.FullName;

    static string GetDecimateScriptPath() =>
        Path.GetFullPath(Path.Combine(GetProjectRoot(), DecimateScriptRelative.Replace('/', Path.DirectorySeparatorChar)));

    static string GetFullAssetPath(string assetPath) =>
        Path.GetFullPath(Path.Combine(GetProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar)));

    static bool TryGetBlenderExecutable(out string path, out string error)
    {
        error = null;
        var fromPrefs = EditorPrefs.GetString(BlenderEditorPrefsKey, string.Empty).Trim().Trim('"');
        if (!string.IsNullOrEmpty(fromPrefs))
        {
            if (!File.Exists(fromPrefs))
            {
                path = null;
                error =
                    $"EditorPrefs key '{BlenderEditorPrefsKey}' must be the full path to the Blender executable.\n" +
                    $"File not found:\n{fromPrefs}";
                return false;
            }

            path = fromPrefs;
            return true;
        }

        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            const string mac = "/Applications/Blender.app/Contents/MacOS/Blender";
            if (File.Exists(mac))
            {
                path = mac;
                return true;
            }
        }

        path = Application.platform == RuntimePlatform.WindowsEditor ? "blender.exe" : "blender";
        return true;
    }

    static bool RunBlenderOnce(string blenderExe, string scriptPath, string assetPath, int targetFaceCount, out string error)
    {
        error = null;
        var inputFbx = GetFullAssetPath(assetPath);
        if (!File.Exists(inputFbx))
        {
            error = $"Input not found on disk: {inputFbx}";
            return false;
        }

        var args = new StringBuilder();
        args.Append("--background ");
        args.Append("--python ");
        args.Append('"');
        AppendEscapedForDoubleQuotes(args, scriptPath);
        args.Append("\" -- \"");
        AppendEscapedForDoubleQuotes(args, inputFbx);
        args.Append("\" ");
        args.Append(targetFaceCount);

        var psi = new ProcessStartInfo
        {
            FileName = blenderExe,
            Arguments = args.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? GetProjectRoot()
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null)
            {
                error = "Process.Start returned null.";
                return false;
            }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                error = $"Exit code {p.ExitCode}.\n{stderr}\n{stdout}".Trim();
                if (string.IsNullOrEmpty(error))
                    error = $"Exit code {p.ExitCode}.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
                Debug.LogWarning($"Blender stderr ({assetPath}):\n{stderr}");

            return true;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            error =
                $"{e.Message}\n\nIf Blender is not on PATH, set the full executable path in EditorPrefs:\n" +
                $"  {BlenderEditorPrefsKey}\n" +
                "(macOS default /Applications/Blender.app/Contents/MacOS/Blender is used when no override is set.)";
            return false;
        }
        catch (System.Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    static void AppendEscapedForDoubleQuotes(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c == '"')
                sb.Append('\\');
            sb.Append(c);
        }
    }
}
