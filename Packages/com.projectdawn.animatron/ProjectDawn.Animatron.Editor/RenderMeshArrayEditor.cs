using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ProjectDawn.Rendering.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(RenderMeshArrayAuthoring))]
    public class RenderMeshArrayEditor : UnityEditor.Editor
    {
        VisualElement m_Materials;
        uint[] m_Hashes;
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var serialized = serializedObject;

            m_Hashes = new uint[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                var renderMeshArray = (RenderMeshArrayAuthoring)targets[i];
                m_Hashes[i] = GetMeshesHash(renderMeshArray);
            }

            // === Instances ===
            var instancesProp = serialized.FindProperty("m_Instances");
            var instancesField = new PropertyField(instancesProp, "Instances");
            instancesField.TrackPropertyValue(instancesProp, _ =>
            {
                serialized.ApplyModifiedProperties();

                for (int i = 0; i < targets.Length; i++)
                {
                    var renderMeshArray = (RenderMeshArrayAuthoring)targets[i];

                    var hash = GetMeshesHash(renderMeshArray);
                    if (m_Hashes[i] == hash)
                        continue;
                    m_Hashes[i] = hash;

                    renderMeshArray.RecalculateBounds();
                    EditorUtility.SetDirty(target);
                    Debug.Log($"Update {renderMeshArray.Instances[0].Material}");
                }

                UpdateMaterials();
            });
            root.Add(instancesField);

            // === Filtering ===
            var filteringProp = serialized.FindProperty("m_Filter");
            var filteringField = new PropertyField(filteringProp);
            root.Add(filteringField);

            // === Light Probe Usage ===
            var lightProbeProp = serialized.FindProperty("m_LightProbeUsage");
            var lightProbeField = new PropertyField(lightProbeProp);
            root.Add(lightProbeField);

            // === Bounds ===
            var boundsProp = serialized.FindProperty("m_Bounds");
            var boundsField = new PropertyField(boundsProp);
            root.Add(boundsField);

            m_Materials = new VisualElement();
            UpdateMaterials();
            root.Add(m_Materials);

            root.SetEnabled(!Application.isPlaying);

            return root;
        }

        unsafe static uint GetMeshesHash(RenderMeshArrayAuthoring renderMeshArray)
        {
            var data = stackalloc int[renderMeshArray.Instances.Length + 1];

            for (int i = 0; i < renderMeshArray.Instances.Length; i++)
            {
                data[i] = renderMeshArray.Instances[i].Mesh?.GetHashCode() ?? 0;
            }
            data[renderMeshArray.Instances.Length] = renderMeshArray.Instances.Length;

            var hash = math.hash(data, sizeof(int) * (renderMeshArray.Instances.Length + 1));

            return hash;
        }

        void UpdateMaterials()
        {
            m_Materials.Clear();

            if (targets.Length > 1)
                return;

            var renderMeshArray = (RenderMeshArrayAuthoring)target;

            for (int i = 0; i < renderMeshArray.Instances.Length; ++i)
            {
                var instance = renderMeshArray.Instances[i];
                if (instance.Material == null)
                    continue;
                var materialEditor = MaterialEditor.CreateEditor(instance.Material) as MaterialEditor;
                var index = i;
                var imgui = new IMGUIContainer(() =>
                {
                    var evt = Event.current;

                    switch (evt.type)
                    {
                        case EventType.DragUpdated:
                        case EventType.DragPerform:
                            //if (!dropArea.Contains(evt.mousePosition))
                                //return;

                            // Check if we're dragging a Material
                            if (DragAndDrop.objectReferences.Length > 0 &&
                                DragAndDrop.objectReferences[0] is Material mat)
                            {
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                                if (evt.type == EventType.DragPerform)
                                {
                                    DragAndDrop.AcceptDrag();
                                    instance.Material = mat;
                                    renderMeshArray.SetInstance(index, instance);
                                    Debug.Log(mat);
                                    EditorUtility.SetDirty(target);
                                }
                            }
                            break;
                    }

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.Space();
                    materialEditor.DrawHeader();
                    materialEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                });
                m_Materials.Add(imgui);
            }
        }

        [DrawGizmo(GizmoType.Selected)]
        static void DrawGizmos(RenderMeshArrayAuthoring authoring, GizmoType gizmoType)
        {
            if (Application.isPlaying)
                return;

            if (!authoring.enabled)
                return;

            var bounds = authoring.Bounds; // Assumes public getter
            Gizmos.color = Color.gray;
            Gizmos.matrix = authoring.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(bounds.Center, bounds.Extents * 2);
        }
    }

    [CustomPropertyDrawer(typeof(RenderMeshArrayAuthoring.Instance))]
    public class InstanceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            container.AddToClassList(PropertyField.ussClassName);
            container.AddToClassList(PropertyField.inputUssClassName);
            container.AddToClassList(BaseField<int>.alignedFieldUssClassName);

            var materialProp = property.FindPropertyRelative("Material");
            var meshProp = property.FindPropertyRelative("Mesh");
            var subMeshProp = property.FindPropertyRelative("SubMesh");

            var materialField = new PropertyField(materialProp, "");
            materialField.style.flexGrow = 1;

            var meshField = new ObjectField { objectType = typeof(Mesh), bindingPath = meshProp.propertyPath };
            meshField.style.flexGrow = 1;

            var subMeshField = new PopupField<string>(new List<string>() { "0" }, 0);
            subMeshField.style.flexGrow = 0.5f;

            void UpdateSubMeshOptions()
            {
                Mesh mesh = meshProp.objectReferenceValue as Mesh;
                int count = mesh != null ? mesh.subMeshCount : 1;

                var options = new List<string>();
                for (int i = 0; i < count; i++)
                    options.Add($"SubMesh {i}");

                int current = Mathf.Clamp(subMeshProp.intValue, 0, count - 1);
                subMeshField.choices = options;
                subMeshField.index = current;

                subMeshField.RegisterValueChangedCallback(evt =>
                {
                    subMeshProp.intValue = subMeshField.index;
                    subMeshProp.serializedObject.ApplyModifiedProperties();
                });
            }

            meshField.RegisterValueChangedCallback(evt =>
            {
                property.serializedObject.ApplyModifiedProperties();
                UpdateSubMeshOptions();
            });

            UpdateSubMeshOptions();

            container.Add(materialField);
            container.Add(meshField);
            container.Add(subMeshField);
            return container;
        }
    }
}