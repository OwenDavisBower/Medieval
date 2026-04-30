using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectDawn.Animation.Editor
{
    class RigCreatorCreator : UnityEditor.ProjectWindowCallback.EndNameEditAction
    {
        public GameObject SelectedGameObject;
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            File.WriteAllText(pathName, ""); // Create an empty rig file
            AssetDatabase.ImportAsset(pathName);

            var newAsset = AssetDatabase.LoadAssetAtPath<Object>(pathName);

            var importer = AssetImporter.GetAtPath(pathName) as RigImporter;
            if (importer != null)
            {
                importer.Prefab = SelectedGameObject;
                importer.CollectRenderers();
                importer.CollectBones();
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            ProjectWindowUtil.ShowCreatedAsset(newAsset);
        }

        [MenuItem("Assets/Create/Rig", priority = 150)]
        static void CreateRig()
        {
            var creator = ScriptableObject.CreateInstance<RigCreatorCreator>();
            creator.SelectedGameObject = Selection.activeGameObject;

            string defaultName;
            if (Selection.activeGameObject == null)
            {
                defaultName = "New Rig.rig";
            }
            else
            {
                defaultName = $"{Selection.activeGameObject.name}.rig";
            }

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, creator, defaultName, null, null);
        }
    }
}
