using UnityEngine;
using UnityEditor;
using System.IO;

public class FBXToPrefabConverter
{
    // 상단 메뉴에 'Tools > Convert FBX to Prefab' 버튼을 만듭니다.
    [MenuItem("Tools/Convert FBX to Prefab")]
    public static void ConvertToPrefab()
    {
        // 현재 프로젝트 창에서 선택된 오브젝트들을 가져옵니다.
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("선택된 오브젝트가 없습니다. Project 창에서 FBX 파일을 선택한 후 실행해주세요.");
            return;
        }

        int convertedCount = 0;

        foreach (GameObject obj in selectedObjects)
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);

            // 선택된 에셋이 FBX 파일인지 확인합니다.
            if (assetPath.ToLower().EndsWith(".fbx"))
            {
                // 프리팹이 저장될 경로 설정 (확장자를 .prefab으로 변경)
                string prefabPath = assetPath.Substring(0, assetPath.Length - 4) + ".prefab";

                // 동일한 이름의 프리팹이 이미 있는지 확인하고 덮어쓸지 묻는 로직을 추가할 수도 있습니다.
                // 여기서는 바로 생성(또는 덮어쓰기)합니다.
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(obj, prefabPath);

                if (prefab != null)
                {
                    convertedCount++;
                }
            }
        }

        // 에셋 데이터베이스를 새로고침하여 생성된 프리팹들을 프로젝트 창에 반영합니다.
        AssetDatabase.Refresh();
        Debug.Log($"총 {convertedCount}개의 FBX 파일이 프리팹으로 변환되었습니다.");
    }
}