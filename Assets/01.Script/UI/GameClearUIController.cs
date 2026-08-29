using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 탈출 성공 시 자연스럽게 페이드인되며 클리어 통계를 보여주는 승리(Game Clear) 결과창 UI 컨트롤러
/// (Root 오브젝트는 항시 Active 상태를 유지하여 이벤트 수신을 보장합니다.)
/// </summary>
public class GameClearUIController : MonoBehaviour
{
    public static GameClearUIController Instance { get; private set; }

    [Header("페이드 연출 설정")]
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("UI 컴포넌트 바인딩")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI clearTimeText;
    [SerializeField] private TextMeshProUGUI suckedBloodText;
    [SerializeField] private TextMeshProUGUI scoreText;

    [Header("배경 스프라이트 바인딩")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite clearSprite;

    [Header("버튼 바인딩")]
    [SerializeField] private Button restartButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button gameEndButton;

    private Coroutine fadeInCoroutine;
    private Canvas parentCanvas;

    private void Awake()
    {
        // 1. 싱글톤 보장
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        parentCanvas = GetComponent<Canvas>();

        // RectTransform 앵커 전체 화면 맞춤
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        // 컴포넌트 자동 바인딩 (Awake 시 1회만 수행하여 성능 최적화)
        BindComponents();

        // [핵심] 씬 시작 시 눈에 보이지 않도록 시각 요소만 안전하게 끔
        HideUIImmediately();
    }

    private void Start()
    {
        EnsureBackgroundSprite();
    }

    private void OnEnable()
    {
        // Root 오브젝트가 Active 상태이므로 OnEnable이 씬 로드 즉시 정상 실행됨!
        EscapeSystem.OnGameClear += ShowGameClearUI;
        EnsureBackgroundSprite();
    }

    private void OnDisable()
    {
        // 메모리 누수 및 dangling reference 방지
        EscapeSystem.OnGameClear -= ShowGameClearUI;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BindComponents();
        EnsureBackgroundSprite();
    }
#endif

    /// <summary>
    /// 게임 시작 시 UI를 화면에서 완벽히 숨깁니다 (Draw Call 및 터치 차단).
    /// </summary>
    private void HideUIImmediately()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (parentCanvas != null)
        {
            parentCanvas.enabled = false;
        }
    }

    /// <summary>
    /// 안전한 스프라이트 배정 (Inspector 지정 우선 -> Resources 안전 로드)
    /// </summary>
    private void EnsureBackgroundSprite()
    {
        if (backgroundImage == null)
        {
            var t = FindChildRecursive(transform, "Image", "Background", "배경");
            if (t != null) backgroundImage = t.GetComponent<Image>();
        }

        if (backgroundImage != null)
        {
            if (clearSprite != null)
            {
                backgroundImage.sprite = clearSprite;
                backgroundImage.color = Color.white;
                return;
            }

            // Resources 폴백 (실제 빌드 환경에서 가장 안전한 로딩 방식)
            var resSprite = Resources.Load<Sprite>("ui/gameclear") ?? Resources.Load<Sprite>("gameclear");
            if (resSprite != null)
            {
                clearSprite = resSprite;
                backgroundImage.sprite = resSprite;
                backgroundImage.color = Color.white;
            }
        }
    }

    /// <summary>
    /// 모기 탈출 성공 시 게임 클리어(승리) 화면 출력
    /// </summary>
    public void ShowGameClearUI()
    {
        // 캔버스 및 레이어 활성화
        if (parentCanvas != null) parentCanvas.enabled = true;
        transform.SetAsLastSibling();

        UpdateStats();

        // 승리 효과음 재생
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Victory);

        if (fadeInCoroutine != null) StopCoroutine(fadeInCoroutine);
        fadeInCoroutine = StartCoroutine(FadeInRoutine());
    }

    /// <summary>
    /// 클리어 시간, 흡혈량, 최종 점수를 정밀 계산 및 UI 갱신
    /// </summary>
    private void UpdateStats()
    {
        float survivalSec = 0f;

        if (PlayTimerManager.Instance != null)
        {
            if (PlayTimerManager.Instance.IsRunning)
            {
                PlayTimerManager.Instance.StopTimerAndFreeze();
            }
            survivalSec = PlayTimerManager.Instance.ElapsedTime;
        }
        else if (BloodManager.Instance != null)
        {
            survivalSec = BloodManager.Instance.SurvivalTime;
        }
        else
        {
            survivalSec = Time.timeSinceLevelLoad;
        }

        // 초 단위 변환 (MM:SS)
        int minutes = Mathf.FloorToInt(survivalSec / 60f);
        int seconds = Mathf.FloorToInt(survivalSec % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        if (clearTimeText != null) clearTimeText.text = timeStr;

        // 흡혈량 및 최종 점수 계산
        float suckedBlood = BloodManager.Instance != null ? BloodManager.Instance.TotalSuckedBlood : 0f;
        int suckedInt = Mathf.RoundToInt(suckedBlood);
        if (suckedBloodText != null) suckedBloodText.text = $"{suckedInt} ml";

        // 공식: 점수 = (생존초 * 10) + (흡혈량 * 20) + 클리어 보너스 1000점
        int totalScore = (Mathf.FloorToInt(survivalSec) * 10) + (suckedInt * 20) + 1000;
        if (scoreText != null) scoreText.text = $"{totalScore:N0} 점";

        Debug.Log($"<color=green>[GameClearUIController]</color> 클리어 통계 바인딩 완료 - 시간: {timeStr}, 점수: {totalScore}");
    }

    /// <summary>
    /// CanvasGroup Alpha를 0에서 1로 부드럽게 페이드인하는 코루틴
    /// </summary>
    private IEnumerator FadeInRoutine()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;

        float timer = 0f;
        while (timer < fadeInDuration)
        {
            // Time.timeScale 영향 없이 작동하도록 unscaledDeltaTime 사용
            timer += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(timer / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = 1.0f;
        canvasGroup.interactable = true;
        Debug.Log("<color=green>[GameClearUIController] 클리어 화면 페이드인 연출 완료</color>");
    }

    private void BindComponents()
    {
        EnsureBackgroundSprite();

        var scoreValueContainer = FindChildRecursive(transform, "점수위치");
        if (scoreValueContainer != null)
        {
            for (int i = 0; i < scoreValueContainer.childCount; i++)
            {
                var child = scoreValueContainer.GetChild(i);
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null) continue;

                if (child.name.Contains("시간") || child.name.Contains("Time") || child.name.Contains("생존") || child.name.Contains("클리어"))
                {
                    clearTimeText = tmp;
                }
                else if (child.name.Contains("흡혈") || child.name.Contains("Blood") || child.name.Contains("피"))
                {
                    suckedBloodText = tmp;
                }
                else if (child.name.Contains("점수") || child.name.Contains("Score"))
                {
                    scoreText = tmp;
                }
            }
        }

        if (clearTimeText == null)
        {
            var t = FindChildRecursive(transform, "Time", "클리어시간", "생존시간", "시간");
            if (t != null) clearTimeText = t.GetComponent<TextMeshProUGUI>();
        }

        if (suckedBloodText == null)
        {
            var t = FindChildRecursive(transform, "Blood", "흡혈량", "피");
            if (t != null) suckedBloodText = t.GetComponent<TextMeshProUGUI>();
        }

        if (scoreText == null)
        {
            var t = FindChildRecursive(transform, "Score", "점수");
            if (t != null) scoreText = t.GetComponent<TextMeshProUGUI>();
        }

        if (titleText == null)
        {
            var t = FindChildRecursive(transform, "Title", "GameClearText", "제목");
            if (t != null) titleText = t.GetComponent<TextMeshProUGUI>();
        }

        // 버튼 클릭 이벤트 중복 바인딩 방지 처리
        if (restartButton == null)
        {
            var t = FindChildRecursive(transform, "restart", "재시작", "retry");
            if (t != null) restartButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
        }
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        if (lobbyButton == null)
        {
            var t = FindChildRecursive(transform, "lobby", "로비", "title", "main");
            if (t != null) lobbyButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
        }
        if (lobbyButton != null)
        {
            lobbyButton.onClick.RemoveAllListeners();
            lobbyButton.onClick.AddListener(OnLobbyClicked);
        }

        if (gameEndButton == null)
        {
            var t = FindChildRecursive(transform, "gameend", "종료", "exit", "quit", "end");
            if (t != null) gameEndButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
        }
        if (gameEndButton != null)
        {
            gameEndButton.onClick.RemoveAllListeners();
            gameEndButton.onClick.AddListener(OnGameEndClicked);
        }
    }

    private Transform FindChildRecursive(Transform parent, params string[] candidateNames)
    {
        foreach (Transform child in parent)
        {
            foreach (var name in candidateNames)
            {
                if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }

            var sub = FindChildRecursive(child, candidateNames);
            if (sub != null) return sub;
        }
        return null;
    }

    public void OnRestartClicked()
    {
        Time.timeScale = 1.0f;
        string currentScene = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentScene);
    }

    public void OnLobbyClicked()
    {
        Time.timeScale = 1.0f;
        AudioManager.Instance?.PlayLobbyBGM();
        SceneManager.LoadScene("Title");
    }

    public void OnGameEndClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}