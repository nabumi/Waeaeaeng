using UnityEngine;
using UnityEngine.InputSystem;

public class PauseManager : MonoBehaviour
{
    [SerializeField] private GameObject pauseMenuUI;
    private bool isPaused = false;

    // ActionMap: UI -> Pause (Button: ESC)
    public void OnPause(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        isPaused = !isPaused;
        pauseMenuUI.SetActive(isPaused);

        // 게임 일시정지 처리
        Time.timeScale = isPaused ? 0f : 1f;
        Debug.Log(isPaused ? "게임 일시정지" : "게임 재개");
    }
}