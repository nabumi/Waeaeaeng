using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 종료 시(사망 또는 탈출 성공) 자연스럽게 페이드인되는 통합 결과창 UI 컨트롤러
/// (Root 오브젝트는 항시 활성화 상태를 유지하여 이벤트 수신을 보장합니다.)
/// </summary>
public class GameOverUIController : MonoBehaviour
{
    public static GameOverUIController Instance { get; private set; }

    [Header("페이드 & 연출 설정")]
    [SerializeField] private float fadeInDuration = 0.6f;
    [Tooltip("모기 사망 후 결과 UI가 뜨기까지의 대기 시간(초)")]
    [SerializeField] private float deathShowDelay = 0.8f;

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
    private Coroutine delayShowCoroutine;
    private Canvas parentCanvas;

    private void Awake()
    {
        // 1. 싱글톤 인스턴스 즉시 등록
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

        BindComponents();

        // [핵심] 게임 시작 시 눈에 보이지 않도록 시각 요소만 안전하게 끔 ($\alpha = 0$)
        HideUIImmediately();
    }

    private void OnEnable()
    {
        // Root가 Active 상태이므로 OnEnable이 정상 호출되어 이벤트에 올바르게 등록됨
        MosquitoController.OnMosquitoDied += OnMosquitoDiedHandler;
    }

    private void OnDisable()
    {
        MosquitoController.OnMosquitoDied -= OnMosquitoDiedHandler;
    }

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

    private void OnMosquitoDiedHandler()
    {
        ShowGameOverUI();
    }

    public void ShowGameOverUI()
    {
        if (delayShowCoroutine != null) StopCoroutine(delayShowCoroutine);
        delayShowCoroutine = StartCoroutine(CoShowGameOverWithDelay());
    }

    private IEnumerator CoShowGameOverWithDelay()
    {
        // 모기 사망 연출 감상 대기 ($t_{\text{delay}} = 0.8\text{s}$)
        yield return new WaitForSecondsRealtime(deathShowDelay);
        OpenResultPanel(isClear: false);
    }

    public void ShowGameClearUI()
    {
        if (GameClearUIController.Instance != null && GameClearUIController.Instance != this)
        {
            GameClearUIController.Instance.ShowGameClearUI();
            return;
        }

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Victory);
        OpenResultPanel(isClear: true);
    }

    private void OpenResultPanel(bool isClear)
    {
        // Canvas 및 시각 레이어 켜기
        if (parentCanvas != null) parentCanvas.enabled = true;
        transform.SetAsLastSibling();

        UpdateStats();

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

        if (fadeInCoroutine != null) StopCoroutine(fadeInCoroutine);
        fadeInCoroutine = StartCoroutine(FadeInRoutine());
    }

    private void UpdateStats()
    {
        float survivalSec = BloodManager.Instance != null ? BloodManager.Instance.SurvivalTime : Time.timeSinceLevelLoad;
        int minutes = Mathf.FloorToInt(survivalSec / 60f);
        int seconds = Mathf.FloorToInt(survivalSec % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        if (survivalTimeText != null) survivalTimeText.text = timeStr;

        float suckedBlood = BloodManager.Instance != null ? BloodManager.Instance.TotalSuckedBlood : 0f;
        int suckedInt = Mathf.RoundToInt(suckedBlood);
        string bloodStr = $"{suckedInt} ml";

        if (suckedBloodText != null) suckedBloodText.text = bloodStr;

        int totalScore = (Mathf.FloorToInt(survivalSec) * 10) + (suckedInt * 20);
        if (scoreText != null) scoreText.text = $"{totalScore:N0} 점";
    }

    private IEnumerator FadeInRoutine()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;

        float timer = 0f;
        while (timer < fadeInDuration)
        {
            timer += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(timer / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = 1.0f;
        canvasGroup.interactable = true;
    }

    private void BindComponents()
    {
        var scoreValueContainer = FindChildRecursive(transform, "점수위치");
        if (scoreValueContainer != null)
        {
            for (int i = 0; i < scoreValueContainer.childCount; i++)
            {
                var child = scoreValueContainer.GetChild(i);
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null) continue;

                if (child.name.Contains("생존") || child.name.Contains("Time") || child.name.Contains("시간")) survivalTimeText = tmp;
                else if (child.name.Contains("흡혈") || child.name.Contains("Blood") || child.name.Contains("피")) suckedBloodText = tmp;
                else if (child.name.Contains("점수") || child.name.Contains("Score")) scoreText = tmp;
            }
        }

        if (survivalTimeText == null) { var t = FindChildRecursive(transform, "Time", "생존시간", "시간"); if (t != null) survivalTimeText = t.GetComponent<TextMeshProUGUI>(); }
        if (suckedBloodText == null) { var t = FindChildRecursive(transform, "Blood", "흡혈량", "피"); if (t != null) suckedBloodText = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = FindChildRecursive(transform, "Score", "점수"); if (t != null) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (titleText == null) { var t = FindChildRecursive(transform, "Title", "GameOverText", "제목", "Text (TMP)"); if (t != null) titleText = t.GetComponent<TextMeshProUGUI>(); }
        if (subTitleText == null) { var t = FindChildRecursive(transform, "사망", "SubTitle", "상태"); if (t != null) subTitleText = t.GetComponent<TextMeshProUGUI>(); }

        if (restartButton == null) { var t = FindChildRecursive(transform, "restart", "재시작", "retry"); if (t != null) restartButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>(); }
        if (restartButton != null) { restartButton.onClick.RemoveAllListeners(); restartButton.onClick.AddListener(OnRestartClicked); }

        if (lobbyButton == null) { var t = FindChildRecursive(transform, "lobby", "로비", "title", "main"); if (t != null) lobbyButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>(); }
        if (lobbyButton != null) { lobbyButton.onClick.RemoveAllListeners(); lobbyButton.onClick.AddListener(OnLobbyClicked); }

        if (gameEndButton == null) { var t = FindChildRecursive(transform, "gameend", "종료", "exit", "quit", "end"); if (t != null) gameEndButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>(); }
        if (gameEndButton != null) { gameEndButton.onClick.RemoveAllListeners(); gameEndButton.onClick.AddListener(OnGameEndClicked); }
    }

    private Transform FindChildRecursive(Transform parent, params string[] candidateNames)
    {
        foreach (Transform child in parent)
        {
            foreach (var name in candidateNames)
            {
                if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return child;
            }
            var sub = FindChildRecursive(child, candidateNames);
            if (sub != null) return sub;
        }
        return null;
    }

    public void OnRestartClicked()
    {
        Time.timeScale = 1.0f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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