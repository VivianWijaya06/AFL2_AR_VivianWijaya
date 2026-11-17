using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PentaWay.EditorTools
{
    public static class PentaWayFloorMenu
    {
        public const string MaterialFolder = "Assets/PentaWay/Materials";

        [MenuItem("PentaWay/Generate Floor Model")]
        static void GenerateFloorModel()
        {
            Selection.activeGameObject = GenerateFloor().gameObject;
        }

        [MenuItem("PentaWay/Rebuild NavMesh Preview")]
        static void RebuildNavMesh()
        {
            FloorModel model = Object.FindAnyObjectByType<FloorModel>(FindObjectsInactive.Include);
            if (model == null)
            {
                Debug.LogError("[PentaWay] No FloorModel in the scene. Run PentaWay > Generate Floor Model first.");
                return;
            }
            model.BuildNavMesh();
            SceneView.RepaintAll();
        }

        public static FloorModel GenerateFloor()
        {
            FloorModel model = Object.FindAnyObjectByType<FloorModel>(FindObjectsInactive.Include);
            if (model == null)
            {
                var go = new GameObject("FloorModel_Lantai5");
                Undo.RegisterCreatedObjectUndo(go, "Create PentaWay floor model");
                model = go.AddComponent<FloorModel>();
            }

            model.floorMaterial = LoadOrCreateMaterial("Floor", null, new Color(0.85f, 0.85f, 0.80f));
            model.wallMaterial = LoadOrCreateMaterial("Wall", null, new Color(0.55f, 0.70f, 0.85f));
            model.blockMaterial = LoadOrCreateMaterial("Block", null, new Color(0.55f, 0.55f, 0.55f));
            model.referencePointMaterial = LoadOrCreateMaterial("Point", null, new Color(0.85f, 0.20f, 0.20f));
            model.Generate();
            model.BuildNavMesh();

            EditorUtility.SetDirty(model);
            EditorSceneManager.MarkSceneDirty(model.gameObject.scene);
            return model;
        }

        public static Material LoadOrCreateMaterial(string name, string shaderName, Color color)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            if (shaderName == null)
            {
                material = FloorModel.MakePreviewMaterial(name, color);
            }
            else
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError("[PentaWay] Shader '" + shaderName + "' not found.");
                    return null;
                }
                material = new Material(shader) { name = name };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
