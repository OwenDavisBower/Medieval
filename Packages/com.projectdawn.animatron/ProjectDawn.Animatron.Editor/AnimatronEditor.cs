using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEngine;
using ProjectDawn.Animation.Hybrid;

namespace ProjectDawn.Animation.Editor
{
    [CustomEditor(typeof(AnimatronAuthoring))]
    public class AnimatronEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var serialized = serializedObject;

            var rigField = new PropertyField(serialized.FindProperty("m_Rig"));
            root.Add(rigField);

            var skinsField = new PropertyField(serialized.FindProperty("m_Skins"));
            root.Add(skinsField);

            var playerField = new PropertyField(serialized.FindProperty("m_Player"));
            root.Add(playerField);

            var cullingModeField = new PropertyField(serialized.FindProperty("m_CullingMode"));
            root.Add(cullingModeField);

            root.SetEnabled(!Application.isPlaying);

            return root;
        }
    }

    [CustomPropertyDrawer(typeof(AnimatronAuthoring.Skin))]
    public class InstanceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var bindingIndex = new PropertyField(property.FindPropertyRelative("BindingIndex"));
            bindingIndex.style.flexGrow = 1;

            var gameObject = new PropertyField(property.FindPropertyRelative("GameObject"));
            gameObject.style.flexGrow = 1;

            container.Add(bindingIndex);
            container.Add(gameObject);
            return container;
        }
    }
}