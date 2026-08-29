using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 종료 시(사망 또는 탈출 성공) 자연스럽게 페이드인되는 통합 결과창 UI 컨트롤러
/// (사망 모드: GAME OVER / 승리 모드: GAME CLEAR 완벽 분기 지원)
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class GameOverUIController : MonoBehaviour
{
    public static GameOverUIController Instance { get; private set; }

    [Header("페이드 연출 설정")]
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("UI 컴포넌트 바인딩")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subTitleText;
    [SerializeField] private TextMeshProUGUI survivalTimeText;
    [SerializeField] private TextMeshProUGUI suckedBloodText;
    [SerializeField] private TextMeshProUGUI scoreText;

    [Header("버튼 바인딩")]
    [SerializeField] private Button restartButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button gameEndButton;

    private Coroutine fadeInCoroutine;
    private Canvas parentCanvas;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 중복 CanvasScaler가 있으면 부모 Scaler와 충돌하므로 제거
        var duplicateScaler = GetComponent<CanvasScaler>();
        if (duplicateScaler != null)
        {
            Destroy(duplicateScaler);
        }

        // 최상단 렌더링 보장 (sortingOrder 500)
        parentCanvas = GetComponent<Canvas>();
        if (parentCanvas == null) parentCanvas = gameObject.AddComponent<Canvas>();
        parentCanvas.overrideSorting = true;
        parentCanvas.sortingOrder = 500;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // 화면 전체 영역으로 RectTransform 강제 확장
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        EnsureCanvasGroup();
        BindComponents();
    }

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
        {
            if (!TryGetComponent<CanvasGroup>(out canvasGroup))
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void Start()
    {
        if (Instance == null) Instance = this;
    }

    private void OnEnable()
    {
        MosquitoController.OnGameOver -= ShowGameOverUI;
        MosquitoController.OnGameOver += ShowGameOverUI;
    }

    private void OnDisable()
    {
        MosquitoController.OnGameOver -= ShowGameOverUI;
    }

    public void BindComponents()
    {
        // 1. "점수위치" 자식 오브젝트들에서 실제 값 텍스트를 정밀 탐색
        var scoreValueContainer = FindChildRecursive(transform, "점수위치");
        if (scoreValueContainer != null)
        {
            for (int i = 0; i < scoreValueContainer.childCount; i++)
            {
                var child = scoreValueContainer.GetChild(i);
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null) continue;

                if (child.name.Contains("생존") || child.name.Contains("Time") || child.name.Contains("시간"))
                {
                    survivalTimeText = tmp;
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

        // 예비 fallback 텍스트 바인딩
        if (survivalTimeText == null)
        {
            var t = FindChildRecursive(transform, "Time", "생존시간", "시간");
            if (t != null) survivalTimeText = t.GetComponent<TextMeshProUGUI>();
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

        // 제목 및 서브타이틀 텍스트 탐색
        if (titleText == null)
        {
            var t = FindChildRecursive(transform, "Title", "GameOverText", "제목", "Text (TMP)");
            if (t != null) titleText = t.GetComponent<TextMeshProUGUI>();
        }
        if (subTitleText == null)
        {
            var t = FindChildRecursive(transform, "사망", "SubTitle", "상태");
            if (t != null) subTitleText = t.GetComponent<TextMeshProUGUI>();
        }

        // 버튼 바인딩
        if (restartButton == null)
        {
            var t = FindChildRecursive(transform, "restart", "재시작", "retry");
            if (t != null)
            {
                if (!t.TryGetComponent<Button>(out restartButton))
                {
                    restartButton = t.gameObject.AddComponent<Button>();
                }
            }
        }
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        if (lobbyButton == null)
        {
            var t = FindChildRecursive(transform, "lobby", "로비", "title", "main");
            if (t != null)
            {
                if (!t.TryGetComponent<Button>(out lobbyButton))
                {
                    lobbyButton = t.gameObject.AddComponent<Button>();
                }
            }
        }
        if (lobbyButton != null)
        {
            lobbyButton.onClick.RemoveListener(OnLobbyClicked);
            lobbyButton.onClick.AddListener(OnLobbyClicked);
        }

        if (gameEndButton == null)
        {
            var t = FindChildRecursive(transform, "gameend", "종료", "exit", "quit", "end");
            if (t != null)
            {
                if (!t.TryGetComponent<Button>(out gameEndButton))
                {
                    gameEndButton = t.gameObject.AddComponent<Button>();
                }
            }
        }
        if (gameEndButton != null)
        {
            gameEndButton.onClick.RemoveListener(OnGameEndClicked);
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

    /// <summary>
    /// 모기 사망 시 게임오버 화면 출력
    /// </summary>
    public void ShowGameOverUI()
    {
        OpenResultPanel(isClear: false);
    }

    /// <summary>
    /// 모기 탈출 성공 시 게임 클리어(승리) 화면 출력
    /// </summary>
    public void ShowGameClearUI()
    {
        if (GameClearUIController.Instance != null)
        {
            GameClearUIController.Instance.ShowGameClearUI();
            return;
        }

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Victory);
        OpenResultPanel(isClear: true);
    }

    private void OpenResultPanel(bool isClear)
    {
        gameObject.SetActive(true);

        if (parentCanvas == null) parentCanvas = GetComponent<Canvas>();
        if (parentCanvas == null) parentCanvas = gameObject.AddComponent<Canvas>();
        parentCanvas.enabled = true;
        parentCanvas.overrideSorting = true;
        parentCanvas.sortingOrder = 500;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        transform.SetAsLastSibling();

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(true);
        }

        EnsureCanvasGroup();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        BindComponents();
        UpdateStats();

        // 승리 vs 사망 텍스트 분기
        if (isClear)
        {
            if (titleText != null) titleText.text = "GAME CLEAR!";
            if (subTitleText != null) subTitleText.text = "탈출 성공!";
        }
        else
        {
            if (titleText != null) titleText.text = "GAME OVER";
            if (subTitleText != null) subTitleText.text = "사망";
        }

        if (fadeInCoroutine != null)
        {
            StopCoroutine(fadeInCoroutine);
        }
        fadeInCoroutine = StartCoroutine(FadeInRoutine());
    }

    private void UpdateStats()
    {
        float survivalSec = BloodManager.Instance != null ? BloodManager.Instance.SurvivalTime : Time.timeSinceLevelLoad;
        int minutes = Mathf.FloorToInt(survivalSec / 60f);
        int seconds = Mathf.FloorToInt(survivalSec % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        if (survivalTimeText != null)
        {
            survivalTimeText.text = timeStr;
        }

        float suckedBlood = BloodManager.Instance != null ? BloodManager.Instance.TotalSuckedBlood : 0f;
        int suckedInt = Mathf.RoundToInt(suckedBlood);
        string bloodStr = $"{suckedInt} ml";

        if (suckedBloodText != null)
        {
            suckedBloodText.text = bloodStr;
        }

        int totalScore = (Mathf.FloorToInt(survivalSec) * 10) + (suckedInt * 20);
        if (scoreText != null)
        {
            scoreText.text = $"{totalScore:N0} 점";
        }

        Debug.LogWarning($"<color=yellow>[GameOverUIController] 결과창 통계 갱신 -> 시간: {timeStr}, 흡혈량: {bloodStr}, 점수: {totalScore}</color>");
    }

    private IEnumerator FadeInRoutine()
    {
        EnsureCanvasGroup();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        float timer = 0f;
        while (timer < fadeInDuration)
        {
            timer += Time.unscaledDeltaTime;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(0.3f, 1.0f, timer / fadeInDuration);
            }
            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        Debug.Log("<color=green>[GameOverUIController] 결과창 페이드인 완료</color>");
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
