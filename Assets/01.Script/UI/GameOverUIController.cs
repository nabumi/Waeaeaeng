using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 사망 시 1초 뒤 자연스럽게 페이드인되는 게임오버 결과창 UI 컨트롤러
/// (재시작, 로비로, 게임종료 3대 버튼 자동 바인딩 및 기능 지원)
/// </summary>
public class GameOverUIController : MonoBehaviour
{
    public static GameOverUIController Instance { get; private set; }

    [Header("페이드 연출 설정")]
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("UI 바인딩")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button gameEndButton;

    private Coroutine fadeInCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // RectTransform 앵커를 전체 화면(Full Stretch)으로 자동 보정
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // CanvasGroup 자동 구성 및 초기 투명화
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        BindButtons();
    }

    private void BindButtons()
    {
        // 1. 재시작 버튼 바인딩 ("restart", "재시작", "retry")
        if (restartButton == null)
        {
            var btnTrans = FindChildRecursive(transform, "restart", "재시작", "retry");
            if (btnTrans != null)
            {
                restartButton = btnTrans.GetComponent<Button>() ?? btnTrans.gameObject.AddComponent<Button>();
            }
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        // 2. 로비로 버튼 바인딩 ("lobby", "로비", "title", "main")
        if (lobbyButton == null)
        {
            var btnTrans = FindChildRecursive(transform, "lobby", "로비", "title", "main");
            if (btnTrans != null)
            {
                lobbyButton = btnTrans.GetComponent<Button>() ?? btnTrans.gameObject.AddComponent<Button>();
            }
        }

        if (lobbyButton != null)
        {
            lobbyButton.onClick.RemoveListener(OnLobbyClicked);
            lobbyButton.onClick.AddListener(OnLobbyClicked);
        }

        // 3. 게임종료 버튼 바인딩 ("gameend", "종료", "exit", "quit")
        if (gameEndButton == null)
        {
            var btnTrans = FindChildRecursive(transform, "gameend", "종료", "exit", "quit", "end");
            if (btnTrans != null)
            {
                gameEndButton = btnTrans.GetComponent<Button>() ?? btnTrans.gameObject.AddComponent<Button>();
            }
        }

        if (gameEndButton != null)
        {
            gameEndButton.onClick.RemoveListener(OnGameEndClicked);
            gameEndButton.onClick.AddListener(OnGameEndClicked);
        }

        Debug.Log($"<color=cyan>[GameOverUIController] 버튼 바인딩 완료 -> 재시작: {(restartButton != null ? restartButton.name : "null")}, 로비: {(lobbyButton != null ? lobbyButton.name : "null")}, 종료: {(gameEndButton != null ? gameEndButton.name : "null")}</color>");
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

    private void OnEnable()
    {
        MosquitoController.OnGameOver += ShowGameOverUI;
    }

    private void OnDisable()
    {
        MosquitoController.OnGameOver -= ShowGameOverUI;
    }

    public void ShowGameOverUI()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        // 자식 오브젝트 활성화 보장 및 버튼 리바인딩
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(true);
        }

        BindButtons();

        if (fadeInCoroutine != null)
        {
            StopCoroutine(fadeInCoroutine);
        }
        fadeInCoroutine = StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

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
        Debug.Log("<color=green>[GameOverUIController] 게임오버 결과창 페이드인 완료</color>");
    }

    /// <summary>
    /// 재시작: 현재 씬을 다시 로드합니다.
    /// </summary>
    public void OnRestartClicked()
    {
        Debug.Log("<color=green>[GameOverUIController] 재시작 버튼 클릭 -> 인게임 씬 재로드</color>");
        Time.timeScale = 1.0f;
        string currentScene = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentScene);
    }

    /// <summary>
    /// 로비로: 타이틀(Title) 씬을 로드합니다.
    /// </summary>
    public void OnLobbyClicked()
    {
        Debug.Log("<color=green>[GameOverUIController] 로비로 버튼 클릭 -> Title 씬 로드</color>");
        Time.timeScale = 1.0f;
        AudioManager.Instance?.PlayLobbyBGM();
        SceneManager.LoadScene("Title");
    }

    /// <summary>
    /// 게임종료: 게임 어플리케이션을 종료합니다.
    /// </summary>
    public void OnGameEndClicked()
    {
        Debug.Log("<color=red>[GameOverUIController] 게임종료 버튼 클릭 -> 어플리케이션 종료</color>");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
