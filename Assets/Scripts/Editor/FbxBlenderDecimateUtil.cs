using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <c>UtilScripts/decimate_fbx_mesh.py</c> via Blender on an FBX asset path.
/// </summary>
public static class FbxBlenderDecimateUtil
{
    public const string BlenderEditorPrefsKey = "Medieval.BlenderExecutable";
    public const string DecimateScriptRelative = "UtilScripts/decimate_fbx_mesh.py";

    public static string GetProjectRoot() => Directory.GetParent(Application.dataPath)!.FullName;

    public static string GetDecimateScriptPath() =>
        Path.GetFullPath(Path.Combine(GetProjectRoot(), DecimateScriptRelative.Replace('/', Path.DirectorySeparatorChar)));

    public static string GetFullAssetPath(string assetPath) =>
        Path.GetFullPath(Path.Combine(GetProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar)));

    public static bool TryGetBlenderExecutable(out string path, out string error)
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

    public static bool RunDecimateOnce(string blenderExe, string scriptPath, string assetPath, int targetFaceCount, out string error)
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
