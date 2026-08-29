using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadIngame()
    {
        // 튜토리얼 팝업이 존재하면 팝업을 먼저 표시하고, 없을 경우 바로 인게임 로드
        var tutorialPopup = TutorialPopupController.Instance ?? FindAnyObjectByType<TutorialPopupController>(FindObjectsInactive.Include);
        if (tutorialPopup != null)
        {
            tutorialPopup.ShowTutorial(() => DirectLoadIngame());
        }
        else
        {
            DirectLoadIngame();
        }
    }

    public void DirectLoadIngame()
    {
        Time.timeScale = 1.0f;
        SceneManager.LoadScene("Ingame");
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}