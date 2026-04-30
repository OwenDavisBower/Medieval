using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectDawn.Animation.Editor
{
    class AnimatronSettingsProvider : SettingsProvider
    {
        static class Styles
        {
            public static readonly GUIContent AffineTransform = EditorGUIUtility.TrTextContent("Rig Transform Type", "");
            public static readonly GUIStyle lineStyle = new GUIStyle();
            public static readonly GUIStyle centerStyle = new GUIStyle();

            static Styles()
            {
                centerStyle.alignment = TextAnchor.MiddleCenter;

                // Initialize the line style
                lineStyle = new GUIStyle();
                lineStyle.normal.background = EditorGUIUtility.whiteTexture; // Use a white texture as the line color
                lineStyle.margin = new RectOffset(0, 0, 4, 4); // Add some margin to the line
            }
        }

        AnimatronSettings m_Settings;
        UnityEditor.Editor m_Editor;

        public AnimatronSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
            label = "Animatron";
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            m_Settings = AnimatronSettings.Instance;
            m_Editor = UnityEditor.Editor.CreateEditor(m_Settings);
        }

        public override void OnDeactivate()
        {
            if (m_Editor != null)
                Object.DestroyImmediate(m_Editor);
            m_Editor = null;
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.BeginVertical();

            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            m_Settings = (AnimatronSettings)EditorGUILayout.ObjectField(m_Settings, typeof(AnimatronSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                AnimatronSettings.Instance = m_Settings;
                if (m_Editor != null)
                    Object.DestroyImmediate(m_Editor);
                m_Editor = UnityEditor.Editor.CreateEditor(m_Settings);
            }

            GUILayout.Space(10);

            if (m_Settings != null)
            {
                DrawHorizontalLine();

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    m_Editor?.OnInspectorGUI();
                }

                static string FormatNumbers(long bytes)
                {
                    string[] sizes = { "", "K", "M", "G" };
                    double len = bytes;
                    int order = 0;

                    while (len >= 1000 && order < sizes.Length - 1)
                    {
                        order++;
                        len /= 1000;
                    }

                    return $"{len:0.##} {sizes[order]}";
                }

                static string FormatBytes(long bytes)
                {
                    string[] sizes = { "B", "KB", "MB", "GB" };
                    double len = bytes;
                    int order = 0;

                    while (len >= 1024 && order < sizes.Length - 1)
                    {
                        order++;
                        len /= 1024;
                    }

                    return $"{len:0.##} {sizes[order]}";
                }

                EditorGUILayout.LabelField($"Total Joints Currently: {FormatNumbers(AnimatronStats.TotalJoints)} / {FormatNumbers(SkinMatrixBuffer.MaxSize)}", EditorStyles.helpBox);
                EditorGUILayout.LabelField($"VRAM Usage: {FormatBytes(AnimatronStats.SkinMatrixBufferBytes)}", EditorStyles.helpBox);
            }
            else
            {
                EditorGUILayout.HelpBox($"Settings asset can be created by clicking menu item Assets>Create>Graphics>Animatron Settings.", MessageType.Info);
            }

            GUILayout.Space(10);

            DrawHorizontalLine();

            ScriptingDefinePopupField.Draw(Styles.AffineTransform, 
                new string[] { "ANIMATRON_RIGID_TRANSFORM", "ANIMATRON_LOCAL_TRANSFORM", "ANIMATRON_AFFINE_TRANSFORM" },
                new string[] { "Rigid Transform", "Local Transform", "Affine Transform" });

            GUILayout.Space(10);

            EditorGUILayout.EndVertical();
        }

        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider() => new AnimatronSettingsProvider("Project/Animatron", SettingsScope.Project);

        static void DrawHorizontalLine()
        {
            Rect lineRect = GUILayoutUtility.GetRect(GUIContent.none, Styles.lineStyle, GUILayout.Height(1)); // Set the height of the line to 1

            // Check the current skin and adjust the line color accordingly
            if (EditorGUIUtility.isProSkin)
                GUI.color = new Color(0.10196f, 0.10196f, 0.10196f, 1); // For light skin, use a darker gray color
            else
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f); // For dark skin, use a slightly darker gray color

            // Draw the line
            GUI.Box(lineRect, GUIContent.none, Styles.lineStyle);

            // Reset the GUI color
            GUI.color = Color.white;
        }

        static string ExtractComponentName(System.Type componentType)
        {
            var attributes = componentType.GetCustomAttributes(typeof(AddComponentMenu), true);

            if (attributes.Length > 0)
            {
                var addComponentMenuAttribute = attributes[0] as AddComponentMenu;
                if (addComponentMenuAttribute != null)
                {
                    string path = addComponentMenuAttribute.componentMenu;
                    string[] menuItems = path.Split('/');
                    string componentName = menuItems.LastOrDefault();

                    return componentName;
                }
            }

            return ObjectNames.NicifyVariableName(componentType.Name);
        }

        public static class ScriptingDefinePopupField
        {
            public static void Draw(GUIContent label, string[] values, string[] names)
            {
                int index = HasScriptingDefineSymbol(values);

                EditorGUI.BeginChangeCheck();

                index = EditorGUILayout.Popup(Styles.AffineTransform, index, names);

                if (EditorGUI.EndChangeCheck())
                {
                    if (!EditorUtility.DisplayDialog("Confirmation", $"This operation will modify scripting defines by adding/removing define symbol {values[index]}", "Yes", "No"))
                    {
                        return;
                    }

                    foreach (var symbol in values)
                        RemoveScriptingDefineSymbol(symbol);
                    AddScriptingDefineSymbol(values[index]);
                }
            }

            static int HasScriptingDefineSymbol(string[] defineSymbols)
            {
                string defines = GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                for (int i = 0; i < defineSymbols.Length; i++)
                {
                    if (defines.Contains(defineSymbols[i]))
                        return i;
                }
                return 0;
            }

            static void AddScriptingDefineSymbol(string symbol)
            {
                string defines = GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                if (!defines.Contains(symbol))
                {
                    defines += ";" + symbol;
                    SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, defines);
                }
            }

            static void RemoveScriptingDefineSymbol(string symbol)
            {
                string defines = GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                if (defines.Contains(symbol))
                {
                    defines = defines.Replace(";" + symbol, "").Replace(symbol + ";", "").Replace(symbol, "");
                    SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, defines);
                }
            }

            static string GetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup)
            {
#if UNITY_6000_0_OR_NEWER
                return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup));
#else
                return PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
#endif
            }

            static void SetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup, string defines)
            {
#if UNITY_6000_0_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
#endif
            }
        }
    }
}
