using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전역 상태(진행 중, 게임 오버, 게임 클리어) 및 씬 전환을 관리하는 매니저
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        Playing,
        GameOver,
        GameClear
    }

    [Header("게임 상태")]
    [SerializeField] private GameState currentState = GameState.Playing;
    public GameState CurrentState => currentState;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        EnsureUIControllers();
    }

    private void Start()
    {
        AudioManager.Instance?.PlayInGameBGM();
        EnsureUIControllers();
    }

    private void OnEnable()
    {
        MosquitoController.OnGameOver += OnGameOverTriggered;
        EscapeSystem.OnGameClear += OnGameClearTriggered;
    }

    private void OnDisable()
    {
        MosquitoController.OnGameOver -= OnGameOverTriggered;
        EscapeSystem.OnGameClear -= OnGameClearTriggered;
    }

    private void EnsureUIControllers()
    {
        // 1. gameover 오브젝트 찾아서 GameOverUIController 부착 및 시작 시 비활성화 보장
        var allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        GameObject gameoverObj = null;

        foreach (var canvas in allCanvases)
        {
            var t = canvas.transform.Find("gameover");
            if (t != null)
            {
                gameoverObj = t.gameObject;
                break;
            }
        }

        if (gameoverObj == null)
        {
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in allTransforms)
            {
                if (t.name.Equals("gameover", System.StringComparison.OrdinalIgnoreCase))
                {
                    gameoverObj = t.gameObject;
                    break;
                }
            }
        }

        if (gameoverObj != null)
        {
            if (gameoverObj.GetComponent<GameOverUIController>() == null)
            {
                gameoverObj.AddComponent<GameOverUIController>();
            }

            // 게임 시작 시 무조건 비활성화!
            if (currentState == GameState.Playing)
            {
                var canvas = gameoverObj.GetComponent<Canvas>();
                if (canvas != null) canvas.enabled = false;
                gameoverObj.SetActive(false);
            }
        }
    }

    private void Update()
    {
        // 게임오버나 클리어 상태일 때 R키로 빠른 재시작 지원
        if (currentState != GameState.Playing)
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartCurrentScene();
            }
        }
    }

    /// <summary>
    /// 모기 사망 이벤트 수신
    /// </summary>
    private void OnGameOverTriggered()
    {
        if (currentState != GameState.Playing) return;
        currentState = GameState.GameOver;

        Debug.LogWarning("<color=yellow>[GameManager] GAME OVER 트리거 -> 결과창 활성화</color>");

        EnsureUIControllers();

        if (GameOverUIController.Instance != null)
        {
            GameOverUIController.Instance.ShowGameOverUI();
        }
        else
        {
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in allTransforms)
            {
                if (t.name.Equals("gameover", System.StringComparison.OrdinalIgnoreCase))
                {
                    t.gameObject.SetActive(true);
                    var ctrl = t.GetComponent<GameOverUIController>() ?? t.gameObject.AddComponent<GameOverUIController>();
                    ctrl.ShowGameOverUI();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 모기 탈출 성공/승리 이벤트 수신
    /// </summary>
    private void OnGameClearTriggered()
    {
        if (currentState != GameState.Playing) return;
        currentState = GameState.GameClear;

        Debug.LogWarning("<color=green>[GameManager] GAME CLEAR 트리거 -> 승리 결과창 활성화</color>");

        EnsureUIControllers();

        if (GameOverUIController.Instance != null)
        {
            GameOverUIController.Instance.ShowGameClearUI();
        }
        else
        {
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in allTransforms)
            {
                if (t.name.Equals("gameover", System.StringComparison.OrdinalIgnoreCase))
                {
                    t.gameObject.SetActive(true);
                    var ctrl = t.GetComponent<GameOverUIController>() ?? t.gameObject.AddComponent<GameOverUIController>();
                    ctrl.ShowGameClearUI();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 현재 씬 재로드
    /// </summary>
    public void RestartCurrentScene()
    {
        Time.timeScale = 1.0f;
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}