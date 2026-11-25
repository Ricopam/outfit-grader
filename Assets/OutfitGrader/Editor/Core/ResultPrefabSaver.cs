using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 의상을 입힌 아바타를 새 프리팹 에셋으로 저장한다(씬의 아바타와 원래 프리팹 에셋은 그대로).
    /// 비어 있는 스크립트가 있으면 Unity가 저장을 거부하므로 임시 사본에서만 제거하고 저장한다.
    /// </summary>
    public static class ResultPrefabSaver
    {
        public const string OutputFolder = "Assets/OutfitGrader_Output";
        public const string MeshFolder = OutputFolder + "/Meshes";

        /// <returns>저장된 에셋 경로. 실패하면 null.</returns>
        /// <param name="assetName">저장 파일 이름(확장자 제외). 같은 이름이 있으면 번호를 붙인다.</param>
        public static string Save(GameObject result, string assetName, out int removedMissingScripts)
        {
            removedMissingScripts = 0;
            if (result == null) return null;

            EnsureFolder();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{assetName}.prefab");
            GameObject temp = Object.Instantiate(result);
            try
            {
                foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
                {
                    removedMissingScripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }
                PersistSceneMeshes(temp);
                PrefabUtility.SaveAsPrefabAsset(temp, path, out bool success);
                return success ? path : null;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder)) return;
            AssetDatabase.CreateFolder("Assets", "OutfitGrader_Output");
        }

        /// <summary>
        /// 씬에만 있는 메시(관통 보정으로 만든 메시 등)를 에셋으로 저장한다. 프리팹은 에셋만 참조할 수 있어서,
        /// 저장하지 않으면 프리팹에서 메시가 빠진다. 맞추기 시점이 아니라 저장 시점에 만들어 Undo 뒤 고아 에셋이 남지 않게 한다.
        /// </summary>
        static void PersistSceneMeshes(GameObject root)
        {
            IEnumerable<Mesh> meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh)
                .Concat(root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh));
            foreach (Mesh mesh in meshes.Where(m => m != null && !EditorUtility.IsPersistent(m)).Distinct())
            {
                if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(OutputFolder, "Meshes");
                // 이름 없는 메시는 확장자만 남은 경로가 되어 저장에 실패하므로 기본 이름을 쓴다.
                string name = string.IsNullOrWhiteSpace(mesh.name) ? "Mesh" : mesh.name;
                string safeName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{safeName}.asset"));
            }
        }
    }
}
