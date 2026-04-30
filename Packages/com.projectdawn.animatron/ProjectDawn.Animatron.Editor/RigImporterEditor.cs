using System.Linq;
using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities.UniversalDelegates;

namespace ProjectDawn.Animation.Editor
{
    [CustomEditor(typeof(RigImporter))]
    public class RigImporterEditor : ScriptedImporterEditor
    {
        VisualElement m_Joints;
        VisualElement m_Skins;
        ListView m_AnimationList;
        VisualElement m_AnimationDetail;
        VisualElement m_AnimationPreview;

        UnityEditor.Editor m_PreviewEditor;

        int m_SelectedAnimationIndex = 0;

        static int SelectedTabIndex
        {
            get
            {
                return EditorPrefs.GetInt("ProjectDawn.Animatron.RigImporterEditor.SelectedTabIndex");
            }
            set
            {
                EditorPrefs.SetInt("ProjectDawn.Animatron.RigImporterEditor.SelectedTabIndex", value);
            }
        }

        void RebuildSkinView()
        {
            var so = serializedObject;
            var skinsProperty = so.FindProperty("Skins");
            var container = m_Skins;

            container.Clear();

            if (skinsProperty.arraySize == 0)
            {
                container.Add(new Label("Empty") { style = { marginLeft = 5 } });
                return;
            }

            for (int i = 0; i < skinsProperty.arraySize; i++)
            {
                var element = skinsProperty.GetArrayElementAtIndex(i);
                var enabledProp = element.FindPropertyRelative("Enabled");
                var rendererProp = element.FindPropertyRelative("SkinnedMeshRenderer");

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                var toggle = new Toggle { value = enabledProp.boolValue };
                var objectField = new ObjectField
                {
                    objectType = typeof(SkinnedMeshRenderer),
                    value = rendererProp.objectReferenceValue,
                    style = { marginLeft = 5, flexGrow = 1 }
                };

                toggle.RegisterValueChangedCallback(evt =>
                {
                    enabledProp.boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    objectField.SetEnabled(enabledProp.boolValue);
                });

                row.Add(toggle);
                row.Add(objectField);
                container.Add(row);
            }
        }

        void RebuildJointView()
        {
            var so = serializedObject;
            var jointsProperty = so.FindProperty("Joints");
            var container = m_Joints;

            container.Clear();

            if (jointsProperty.arraySize == 0)
            {
                container.Add(new Label("Empty") { style = { marginLeft = 5 } });
                return;
            }

            var stack = new System.Collections.Generic.Stack<Transform>();
            for (int i = 0; i < jointsProperty.arraySize; i++)
            {
                var element = jointsProperty.GetArrayElementAtIndex(i);
                var enabledProp = element.FindPropertyRelative("Enabled");
                var transformProp = element.FindPropertyRelative("Transform");

                var transform = transformProp.objectReferenceValue as Transform;
                while (stack.Count != 0 && transform.parent != stack.Peek())
                    stack.Pop();

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                var objectField = new ObjectField
                {
                    objectType = typeof(Transform),
                    value = transformProp.objectReferenceValue,
                    style = { marginLeft = stack.Count * 15 + 5, flexGrow = 1 }
                };

                var toggle = new Toggle { value = enabledProp.boolValue };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    enabledProp.boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    objectField.SetEnabled(enabledProp.boolValue);
                });

                objectField.SetEnabled(enabledProp.boolValue);

                row.Add(toggle);
                row.Add(objectField);
                container.Add(row);

                stack.Push(transform);
            }
        }

        void RebuildAnimationView()
        {
            var so = serializedObject;
            var animationsProp = so.FindProperty("Animations");

            m_AnimationList.itemsSource = null;
            m_AnimationList.itemsSource = Enumerable.Range(0, animationsProp.arraySize).ToList();

            m_AnimationList.itemsAdded += (indices) =>
            {
                foreach (var index in indices)
                    animationsProp.InsertArrayElementAtIndex(index);
                so.ApplyModifiedProperties();

            };
            m_AnimationList.itemsRemoved += (indices) =>
            {
                foreach (var index in indices)
                    animationsProp.DeleteArrayElementAtIndex(index);
                so.ApplyModifiedProperties();

            };
            m_AnimationList.fixedItemHeight = 16;
            m_AnimationList.makeItem = () => new Label() { style = { paddingLeft = 3 } };
            m_AnimationList.bindItem = (e, i) =>
            {
                var element = animationsProp.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("Name");
                (e as Label).text = nameProp.stringValue;
            };

            m_AnimationList.selectedIndicesChanged += selected =>
            {
                foreach (var index in selected)
                {
                    m_SelectedAnimationIndex = (int)index;
                    ShowAnimationDetail();
                }
            };

            ShowAnimationDetail();
        }

        void ShowAnimationDetail()
        {
            var so = serializedObject;
            var animationsProp = so.FindProperty("Animations");

            m_AnimationDetail.Clear();

            if (m_SelectedAnimationIndex < 0 || m_SelectedAnimationIndex >= animationsProp.arraySize)
                return;

            var iterator = animationsProp.GetArrayElementAtIndex(m_SelectedAnimationIndex);
            var end = iterator.GetEndProperty();

            m_PreviewEditor = CreateEditor(iterator.FindPropertyRelative("Clip").objectReferenceValue);

            iterator.NextVisible(true);
            do
            {
                m_AnimationDetail.Add(new PropertyField(iterator.Copy()));
                m_AnimationDetail.Bind(so);
            }
            while (iterator.NextVisible(false) && !SerializedProperty.EqualContents(iterator, end));
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var importer = (RigImporter)target;
            var so = serializedObject;

            // Tabs
            var toolbar = new Toolbar();
            var tabs = new VisualElement() { style = { marginTop = 5 } };
            root.Add(toolbar);
            root.Add(tabs);

            var prefabTab = new VisualElement();
            var animationsTab = new VisualElement();
            var skinsTab = new VisualElement();
            var jointsTab = new VisualElement();

            // Animations Tab
            {
                var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                m_AnimationList = new ListView
                {
                    style = { minWidth = 100, flexGrow = 0, alignSelf = Align.FlexStart },
                    reorderable = true,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                    showBorder = true,
                    showAddRemoveFooter = true,
                };
                m_AnimationDetail = new VisualElement { name = "animation-details", style = { flexGrow = 3, marginLeft = 15 } };


                container.Add(m_AnimationList);
                container.Add(m_AnimationDetail);

                animationsTab.Add(container);

                m_AnimationPreview = new VisualElement();
                animationsTab.Add(m_AnimationPreview);

                RebuildAnimationView();
            }

            // Skins Tab
            {
                var container = new ScrollView { style = { maxHeight = 520 } };
                m_Skins = container;
                skinsTab.Add(container);

                skinsTab.Add(new PropertyField(so.FindProperty("Force4BlendWeights")));
                RebuildSkinView();
            }

            // Joints Tab
            {
                var container = new ScrollView { style = { maxHeight = 520 } };
                m_Joints = container;
                jointsTab.Add(container);

                jointsTab.Add(new PropertyField(so.FindProperty("JointNames")));
                RebuildJointView();
            }

            // Prefab Tab
            {
                // Prefab field on top
                var prefabProp = so.FindProperty("Prefab");
                var prefabField = new PropertyField(prefabProp);
                prefabField.TrackPropertyValue(prefabProp, _ =>
                {
                    importer.CollectRenderers();
                    importer.CollectBones();
                    so.Update();

                    RebuildJointView();
                    RebuildSkinView();
                    RebuildAnimationView();
                    EditorUtility.SetDirty(importer);
                });
                prefabTab.Add(prefabField);

                prefabTab.Add(new PropertyField(so.FindProperty("PrefabMode")));
            }

            // Keep references to toggles
            ToolbarToggle prefabToggle = new ToolbarToggle() { text = "Prefab" };
            ToolbarToggle animToggle = new ToolbarToggle() { text = "Animations" };
            ToolbarToggle skinsToggle = new ToolbarToggle() { text = "Skins" };
            ToolbarToggle jointsToggle = new ToolbarToggle() { text = "Joints" };

            // Helper to show tab and update toggles
            void ShowTab(VisualElement tab)
            {
                tab.Bind(so);

                tabs.Clear();
                tabs.Add(tab);

                prefabToggle.value = (tab == prefabTab);
                animToggle.value = (tab == animationsTab);
                skinsToggle.value = (tab == skinsTab);
                jointsToggle.value = (tab == jointsTab);

            }

            // Register toggle events
            prefabToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                    return;
                ShowTab(prefabTab);
                SelectedTabIndex = 0;
            });
            animToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                    return;
                ShowTab(animationsTab);
                SelectedTabIndex = 1;
            });
            skinsToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                    return;
                ShowTab(skinsTab);
                SelectedTabIndex = 2;
            });
            jointsToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                    return;
                ShowTab(jointsTab);
                SelectedTabIndex = 3;
            });

            // Add toggles to toolbar
            toolbar.Add(prefabToggle);
            toolbar.Add(animToggle);
            toolbar.Add(skinsToggle);
            toolbar.Add(jointsToggle);

            // Show default tab
            switch (SelectedTabIndex)
            {
                default: ShowTab(prefabTab); break;
                case 1:  ShowTab(animationsTab); break;
                case 2:  ShowTab(skinsTab); break;
                case 3:  ShowTab(jointsTab); break;
            };

            // Prefab mode + Apply/Revert
            root.Add(new IMGUIContainer(() => ApplyRevertGUI()));

            return root;
        }

        public override bool HasPreviewGUI() => SelectedTabIndex == 1 && m_PreviewEditor != null && m_PreviewEditor.HasPreviewGUI();
        public override void OnPreviewGUI(Rect r, GUIStyle background) => m_PreviewEditor?.OnPreviewGUI(r, background);
        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background) => m_PreviewEditor?.OnInteractivePreviewGUI(r, background);
        public override void DrawPreview(Rect previewArea) => m_PreviewEditor?.DrawPreview(previewArea);
    }


    public static class EventTypeManager
    {
        static Type[] m_Types;
        static string[] m_Names;

        public static Type[] Types
        {
            get
            {
                Initialize();
                return m_Types;
            }
        }

        public static string[] Names
        {
            get
            {
                Initialize();
                return m_Names;
            }
        }

        public static bool TryGetTypeFromName(string name, out Type type)
        {
            Initialize();

            int index = Array.IndexOf(m_Names, name);
            if (index == -1)
            {
                type = default;
                return false;
            }

            type = m_Types[index];
            return true;
        }

        static void Initialize()
        {
            if (m_Types != null)
                return;

            m_Types = (from asm in AppDomain.CurrentDomain.GetAssemblies()
                           from t in asm.GetTypes()
                           where t.IsValueType && !t.IsPrimitive && !t.IsEnum && typeof(IEventData).IsAssignableFrom(t)
                           select t).ToArray();

            m_Types = new[] { (Type)null }.Concat(m_Types).ToArray();
            m_Names = new[] { "None" }.Concat(m_Types.Skip(1).Select(t => t.Name)).ToArray();
        }
    }


    [CustomPropertyDrawer(typeof(RigImporter.Event))]
    public class EventDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();

            var typeNameProp = property.FindPropertyRelative("TypeName");
            var dataProp = property.FindPropertyRelative("Data");

            // Dropdown for event type
            var popup = new PopupField<string>(
                "Type",
                EventTypeManager.Names.ToList(),
                Math.Max(0, Array.IndexOf(EventTypeManager.Names, typeNameProp.stringValue))
            );
            popup.AddToClassList(PropertyField.ussClassName);
            popup.AddToClassList(PropertyField.inspectorElementUssClassName);
            popup.AddToClassList(BaseField<int>.alignedFieldUssClassName);
            popup.RegisterValueChangedCallback(evt =>
            {
                typeNameProp.stringValue = evt.newValue;
                typeNameProp.serializedObject.ApplyModifiedProperties();

                int newIndex = Array.IndexOf(EventTypeManager.Names, evt.newValue);
                dataProp.managedReferenceValue = newIndex == 0 ? null : Activator.CreateInstance(EventTypeManager.Types[newIndex]);
                dataProp.serializedObject.ApplyModifiedProperties();

                container.MarkDirtyRepaint();
            });
            container.Add(popup);

            container.Add(new PropertyField(property.FindPropertyRelative("Offset")));
            container.Add(new PropertyField(property.FindPropertyRelative("Length")));

            // Data field
            var dataField = new PropertyField(dataProp);
            container.Add(dataField);

            return container;
        }
    }
}
