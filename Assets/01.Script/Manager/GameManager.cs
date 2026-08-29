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

    private GameObject gameoverObj;
    private GameObject gameclearObj;

    private GameObject FindSceneObject(string targetName)
    {
        // 1. 활성 씬의 루트 오브젝트 및 자식에서 정밀 탐색 (에셋 폴더 프리팹 제외)
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            var rootObjects = activeScene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (root.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                    return root;
                var child = FindChildRecursive(root.transform, targetName);
                if (child != null) return child.gameObject;
            }
        }

        // 2. 씬에 로드된 캔버스 자식 탐색
        var allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var canvas in allCanvases)
        {
            if (canvas == null || !canvas.gameObject.scene.isLoaded) continue;
            if (canvas.gameObject.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                return canvas.gameObject;
            var child = FindChildRecursive(canvas.transform, targetName);
            if (child != null) return child.gameObject;
        }

        return null;
    }

    private void EnsureUIControllers()
    {
        // 1. gameover 및 gameclear 오브젝트 찾아서 UIController 부착 및 초기 비활성화 보장
        if (gameoverObj == null || !gameoverObj.scene.isLoaded)
        {
            gameoverObj = FindSceneObject("gameover");
        }

        if (gameclearObj == null || !gameclearObj.scene.isLoaded)
        {
            gameclearObj = FindSceneObject("gameclear");
        }

        if (gameoverObj != null)
        {
            if (!gameoverObj.TryGetComponent<GameOverUIController>(out var ctrl))
            {
                ctrl = gameoverObj.AddComponent<GameOverUIController>();
            }

            // 게임 시작 시 무조건 비활성화
            if (currentState == GameState.Playing)
            {
                var canvas = gameoverObj.GetComponent<Canvas>();
                if (canvas != null) canvas.enabled = false;
                gameoverObj.SetActive(false);
            }
        }

        if (gameclearObj != null)
        {
            if (!gameclearObj.TryGetComponent<GameClearUIController>(out var ctrl))
            {
                ctrl = gameclearObj.AddComponent<GameClearUIController>();
            }

            // 게임 시작 시 무조건 비활성화
            if (currentState == GameState.Playing)
            {
                var canvas = gameclearObj.GetComponent<Canvas>();
                if (canvas != null) canvas.enabled = false;
                gameclearObj.SetActive(false);
            }
        }

        // 2. 상단 HUD (타이머 및 흡혈량 카운터) 컨트롤러 연결 보장
        IngameHUDController.EnsureHUDExists();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 에디터/개발 빌드에서 F2 키로 클리어 화면 즉시 테스트 지원
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            Debug.LogWarning("<color=cyan>[치트] F2 입력 -> OnGameClearTriggered() 즉시 테스트 실행</color>");
            OnGameClearTriggered();
        }
#endif
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

        if (gameoverObj != null)
        {
            gameoverObj.SetActive(true);
            var canvas = gameoverObj.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 500;
            }
            if (!gameoverObj.TryGetComponent<GameOverUIController>(out var ctrl))
            {
                ctrl = gameoverObj.AddComponent<GameOverUIController>();
            }
            ctrl.ShowGameOverUI();
        }
        else if (GameOverUIController.Instance != null)
        {
            GameOverUIController.Instance.ShowGameOverUI();
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

        if (gameclearObj != null)
        {
            gameclearObj.SetActive(true);
            var canvas = gameclearObj.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 500;
            }
            if (!gameclearObj.TryGetComponent<GameClearUIController>(out var ctrl))
            {
                ctrl = gameclearObj.AddComponent<GameClearUIController>();
            }
            ctrl.ShowGameClearUI();
        }
        else if (GameClearUIController.Instance != null)
        {
            GameClearUIController.Instance.ShowGameClearUI();
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