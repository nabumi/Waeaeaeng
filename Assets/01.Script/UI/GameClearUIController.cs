using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 탈출 성공 시 자연스럽게 페이드인되며 클리어 통계를 보여주는 승리(Game Clear) 결과창 UI 컨트롤러
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
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
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
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

        // RectTransform 앵커를 전체 화면으로 설정
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
        EnsureBackgroundSprite();
    }

    private void OnEnable()
    {
        EscapeSystem.OnGameClear -= ShowGameClearUI;
        EscapeSystem.OnGameClear += ShowGameClearUI;
        EnsureBackgroundSprite();
    }

    private void OnDisable()
    {
        EscapeSystem.OnGameClear -= ShowGameClearUI;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BindComponents();
        EnsureBackgroundSprite();
    }
#endif

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

            // 파일에서 직접 32-bit PNG 텍스처를 로드하여 스프라이트 생성
            string[] candidatePaths = new string[]
            {
                System.IO.Path.Combine(Application.dataPath, "02.Resource/ui/gameclear.png"),
                System.IO.Path.Combine(Application.dataPath, "Resources/ui/gameclear.png"),
                System.IO.Path.Combine(Application.dataPath, "02.Resource/gameclear.png")
            };

            foreach (var p in candidatePaths)
            {
                if (System.IO.File.Exists(p))
                {
                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(p);
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes))
                        {
                            tex.filterMode = FilterMode.Bilinear;
                            Sprite spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                            spr.name = "gameclear_runtime";
                            clearSprite = spr;
                            backgroundImage.sprite = spr;
                            backgroundImage.color = Color.white;
                            Debug.LogWarning($"<color=green>[GameClearUIController] '{p}' 에서 gameclear 이미지를 정상 로드하여 스프라이트로 배정했습니다! ({tex.width}x{tex.height})</color>");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[GameClearUIController] 이미지 로드 중 예외: {ex.Message}");
                    }
                }
            }

            // Resources.Load 폴백
            var resSprite = Resources.Load<Sprite>("ui/gameclear");
            if (resSprite == null) resSprite = Resources.Load<Sprite>("gameclear");
            if (resSprite != null)
            {
                clearSprite = resSprite;
                backgroundImage.sprite = resSprite;
                backgroundImage.color = Color.white;
            }
        }
    }

    public void BindComponents()
    {
        EnsureBackgroundSprite();

        // 1. "점수위치" 컨테이너에서 값 텍스트 정밀 탐색
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

        // 예비 fallback 텍스트 바인딩
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
    /// 모기 탈출 성공 시 게임 클리어(승리) 화면 출력
    /// </summary>
    public void ShowGameClearUI()
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

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Victory);

        if (fadeInCoroutine != null) StopCoroutine(fadeInCoroutine);
        fadeInCoroutine = StartCoroutine(FadeInRoutine());
    }

    private void UpdateStats()
    {
        float survivalSec = BloodManager.Instance != null ? BloodManager.Instance.SurvivalTime : Time.timeSinceLevelLoad;
        int minutes = Mathf.FloorToInt(survivalSec / 60f);
        int seconds = Mathf.FloorToInt(survivalSec % 60f);
        string timeStr = $"{minutes:00}:{seconds:00}";

        if (clearTimeText != null)
        {
            clearTimeText.text = timeStr;
        }

        float suckedBlood = BloodManager.Instance != null ? BloodManager.Instance.TotalSuckedBlood : 0f;
        int suckedInt = Mathf.RoundToInt(suckedBlood);
        string bloodStr = $"{suckedInt} ml";

        if (suckedBloodText != null)
        {
            suckedBloodText.text = bloodStr;
        }

        // 클리어 보너스 1000점 포함 점수 계산
        int totalScore = (Mathf.FloorToInt(survivalSec) * 10) + (suckedInt * 20) + 1000;
        if (scoreText != null)
        {
            scoreText.text = $"{totalScore:N0} 점";
        }

        Debug.LogWarning($"<color=green>[GameClearUIController] 승리 결과창 통계 갱신 -> 클리어 시간: {timeStr}, 흡혈량: {bloodStr}, 최종 점수: {totalScore}</color>");
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
        Debug.Log("<color=green>[GameClearUIController] 클리어 화면 페이드인 완료</color>");
    }

    private void Update()
    {
        if (gameObject.activeInHierarchy)
        {
            if (Keyboard.current != null && (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
            {
                OnRestartClicked();
            }
        }
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
