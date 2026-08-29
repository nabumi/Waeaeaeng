using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 전역 게임 상태 관리 및 씬 리셋을 전담하는 매니저 클래스
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("게임 상태")]
    [SerializeField] private bool isGameOver = false;

    private void Awake()
    {
        // 싱글톤 패턴 적용 (인스턴스 유일성 보장)
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

    private void OnEnable()
    {
        // MosquitoController 사망 이벤트 정기 구독
        MosquitoController.OnGameOver += OnGameOverTriggered;
    }

    private void OnDisable()
    {
        // 메모리 누수 방지를 위한 이벤트 구독 해제
        MosquitoController.OnGameOver -= OnGameOverTriggered;
    }

    private void Update()
    {
        // 게임오버 상태일 때 R키를 누르면 씬 리셋
        if (isGameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartCurrentScene();
            }
        }
    }

    /// <summary>
    /// 모기 사망 이벤트 수신 시 실행되는 콜백 함수
    /// </summary>
    private void OnGameOverTriggered()
    {
        isGameOver = true;

        Debug.LogWarning("<color=yellow>==================================================</color>");
        Debug.LogWarning("<color=yellow>[GAME OVER] 모기가 사망했습니다! 'R' 키를 누르면 리셋됩니다.</color>");
        Debug.LogWarning("<color=yellow>==================================================</color>");
    }

    /// <summary>
    /// 현재 활성화된 씬을 다시 로드하여 모든 상태를 완벽 초기화합니다.
    /// </summary>
    public void RestartCurrentScene()
    {
        Debug.Log("<color=green>[System] R키 입력 수신: 씬을 다시 로드합니다...</color>");

        // 혹시 시간 정지(Time.timeScale = 0) 처리를 해뒀더라도 정상 속도로 복구
        Time.timeScale = 1.0f;

        // 현재 active 씬의 이름을 추출하여 재로드
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}