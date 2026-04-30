using ProjectDawn.Animation.Hybrid;
using UnityEditor;

namespace ProjectDawn.Animation.Editor
{
    [CustomEditor(typeof(Rig))]
    public class RigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (!serializedObject.isEditingMultipleObjects)
            {
                var rig = target as Rig;
                EditorGUILayout.LabelField($"Size: {FormatBytes(rig.TotalBytes)} Allocated: {rig.IsCreated}", EditorStyles.helpBox);
            }
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
    }
}
