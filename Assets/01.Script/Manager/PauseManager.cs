using UnityEngine;
using UnityEngine.InputSystem;

public class PauseManager : MonoBehaviour
{
    [SerializeField] private GameObject pauseMenuUI;


    // ActionMap: UI -> Pause (Button: ESC)
    public void OnPause(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        TogglePause();
    }

    public void TogglePause()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        if (OptionUIController.Instance != null)
        {
            OptionUIController.Instance.TogglePause();
        }
        else if (pauseMenuUI != null)
        {
            bool isPaused = !pauseMenuUI.activeSelf;
            pauseMenuUI.SetActive(isPaused);
            Time.timeScale = isPaused ? 0f : 1f;
        }
    }
}