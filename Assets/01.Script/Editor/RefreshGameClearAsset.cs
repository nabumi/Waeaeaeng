#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class FixGameClearAssetTool
{
    private const string ASSET_PATH = "Assets/02.Resource/ui/gameclear.png";

    [MenuItem("Tools/Fix GameClear Asset")]
    public static void Refresh()
    {
        Sprite spr = AssetDatabase.LoadAssetAtPath<Sprite>(ASSET_PATH);

        if (spr == null)
        {
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(ASSET_PATH);
            foreach (Object asset in allAssets)
            {
                if (asset is Sprite loadedSprite)
                {
                    spr = loadedSprite;
                    break;
                }
            }
        }

        if (spr == null)
        {
            Debug.LogError($"[FixTool] '{ASSET_PATH}' 경로에서 Sprite를 찾을 수 없습니다.");
            return;
        }

        // =========================================================================
        // [수정 포인트] 
        // FindObjectsSortMode 매개변수를 제거하고 FindObjectsInactive 인자만 전달합니다.
        // 유니티 최신 버전에 맞춘 O(N) 성능의 표준 API 호출 방식입니다.
        // =========================================================================
        Canvas[] sceneCanvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);

        int updatedCount = 0;

        foreach (Canvas canvas in sceneCanvases)
        {
            Transform imageTransform = canvas.transform.Find("gameclear/Image");
            if (imageTransform != null && imageTransform.TryGetComponent<Image>(out var img))
            {
                Undo.RecordObject(img, "Fix GameClear Image Sprite");
                img.sprite = spr;
                img.color = Color.white;
                EditorUtility.SetDirty(img);
                updatedCount++;
            }

            Transform gcTransform = canvas.transform.Find("gameclear");
            if (gcTransform != null && gcTransform.TryGetComponent<CanvasGroup>(out var cg))
            {
                Undo.RecordObject(cg, "Fix GameClear CanvasGroup");
                cg.alpha = 1.0f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
                EditorUtility.SetDirty(cg);
            }
        }

        Debug.Log($"<color=cyan>[GameClear Fix Tool]</color> 총 {updatedCount}개 UI의 스프라이트 연결을 완료했습니다.");
    }
}
#endif