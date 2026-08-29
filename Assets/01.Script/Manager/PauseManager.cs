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
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        isPaused = !isPaused;
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(isPaused);
        }

        // 게임 일시정지 처리
        Time.timeScale = isPaused ? 0f : 1f;
        Debug.Log(isPaused ? "게임 일시정지" : "게임 재개");
    }
}