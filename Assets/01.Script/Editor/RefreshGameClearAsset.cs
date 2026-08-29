#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RefreshGameClearAsset
{
    static RefreshGameClearAsset()
    {
        EditorApplication.delayCall += Refresh;
    }

    [MenuItem("Tools/Fix GameClear Asset")]
    public static void Refresh()
    {
        string path = "Assets/02.Resource/ui/gameclear.png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        
        Sprite spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (spr == null)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var a in all)
            {
                if (a is Sprite s)
                {
                    spr = s;
                    break;
                }
            }
        }

        var sceneCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var c in sceneCanvases)
        {
            var t = c.transform.Find("gameclear/Image");
            if (t != null)
            {
                var img = t.GetComponent<Image>();
                if (img != null && spr != null)
                {
                    img.sprite = spr;
                    img.color = Color.white;
                    EditorUtility.SetDirty(img);
                    EditorUtility.SetDirty(t.gameObject);
                }
            }

            var gc = c.transform.Find("gameclear");
            if (gc != null)
            {
                var cg = gc.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1.0f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                    EditorUtility.SetDirty(cg);
                }
            }
        }

        Debug.LogWarning("<color=green>[GameClear] ??酉?諛??섏씠?대씪???먯뀑 ?덈줈怨좎묠 諛??ㅽ봽?쇱씠???곌껐 ?꾨즺!</color>");
    }
}
#endif
