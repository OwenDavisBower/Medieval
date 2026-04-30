using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEngine;
using ProjectDawn.Animation.Hybrid;

namespace ProjectDawn.Animation.Editor
{
    [CustomEditor(typeof(InertializerAuthoring))]
    public class InertializerEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var serialized = serializedObject;

            var durationField = new PropertyField(serialized.FindProperty("m_Duration"));
            root.Add(durationField);

            root.SetEnabled(!Application.isPlaying);

            return root;
        }
    }
}
