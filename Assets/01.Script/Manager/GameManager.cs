using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전역 상태(진행 중, 게임 오버, 게임 클리어) 관리 및 씬 전환을 관장하는 매니저
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

    [Header("UI 컨트롤러 수동 바인딩 (선택 사항)")]
    [Tooltip("비워둘 경우 각 UI 컨트롤러의 Singleton Instance를 자동 활용합니다.")]
    [SerializeField] private GameOverUIController gameOverUI;
    [SerializeField] private GameClearUIController gameClearUI;

    private void Awake()
    {
        // 싱글톤 데이터 레이어 보장
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // 게임 시작 시 BGM 재생 및 상태 초기화
        AudioManager.Instance?.PlayInGameBGM();
        currentState = GameState.Playing;
        Time.timeScale = 1.0f; // 시간 흐름 정상화 ($\Delta t_{scaled} = \Delta t_{unscaled}$)
    }

    private void OnEnable()
    {
        // [수정 완료] 최신화된 MosquitoController.OnMosquitoDied 이벤트 구독
        MosquitoController.OnMosquitoDied += OnGameOverTriggered;

        // EscapeSystem 게임 클리어 이벤트 구독
        EscapeSystem.OnGameClear += OnGameClearTriggered;
    }

    private void OnDisable()
    {
        // [수정 완료] 메모리 누수 방지를 위한 이벤트 구독 해제
        MosquitoController.OnMosquitoDied -= OnGameOverTriggered;
        EscapeSystem.OnGameClear -= OnGameClearTriggered;
    }

    private void Update()
    {
        // 1. 게임오버 또는 클리어 상태일 때 'R' 키로 빠른 재시작 지원
        if (currentState != GameState.Playing)
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartCurrentScene();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 2. 디버그/에디터 환경: F2 키 입력 시 게임 클리어 테스트
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            Debug.LogWarning("<color=cyan>[치트] F2 입력 -> OnGameClearTriggered() 디버그 실행</color>");
            OnGameClearTriggered();
        }
#endif
    }

    /// <summary>
    /// 모기 사망 이벤트 수신 시 호출
    /// </summary>
    private void OnGameOverTriggered()
    {
        // 이미 게임 종료 상태라면 중복 실행 방지
        if (currentState != GameState.Playing) return;
        currentState = GameState.GameOver;

        Debug.LogWarning("<color=yellow>[GameManager] GAME OVER 상태 전환 -> 사망 결과창 출력 지시</color>");

        // Direct 참조 우선, 없으면 Singleton Instance로 안전하게 접근 (FindObjects 연산 철거)
        if (gameOverUI != null)
        {
            gameOverUI.ShowGameOverUI();
        }
        else if (GameOverUIController.Instance != null)
        {
            GameOverUIController.Instance.ShowGameOverUI();
        }
        else
        {
            Debug.LogError("<color=red>[GameManager] 씬 내에 GameOverUIController를 찾을 수 없습니다!</color>");
        }
    }

    /// <summary>
    /// 모기 탈출 성공(승리) 이벤트 수신 시 호출
    /// </summary>
    private void OnGameClearTriggered()
    {
        // 이미 게임 종료 상태라면 중복 실행 방지
        if (currentState != GameState.Playing) return;
        currentState = GameState.GameClear;

        Debug.LogWarning("<color=green>[GameManager] GAME CLEAR 상태 전환 -> 승리 결과창 출력 지시</color>");

        // Direct 참조 우선, 없으면 Singleton Instance로 안전하게 접근
        if (gameClearUI != null)
        {
            gameClearUI.ShowGameClearUI();
        }
        else if (GameClearUIController.Instance != null)
        {
            GameClearUIController.Instance.ShowGameClearUI();
        }
        else
        {
            Debug.LogError("<color=red>[GameManager] 씬 내에 GameClearUIController를 찾을 수 없습니다!</color>");
        }
    }

    /// <summary>
    /// 현재 씬 재로드 (시간 배율 복구 포함)
    /// </summary>
    public void RestartCurrentScene()
    {
        Time.timeScale = 1.0f;
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}