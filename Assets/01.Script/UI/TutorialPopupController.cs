using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tutorial popup UI controller in Lobby (Title scene)
/// </summary>
public class TutorialPopupController : MonoBehaviour
{
    public static TutorialPopupController Instance { get; private set; }

    [Header("Fade Settings")]
    [SerializeField] private float fadeInDuration = 0.3f;

    [Header("UI Component Bindings")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI guideText;
    [SerializeField] private Image backgroundImage;

    [Header("Button Bindings")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button closeButton;

    private Coroutine fadeCoroutine;
    private Action onStartGameCallback;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        BindComponents();
    }

    private void Start()
    {
        if (Instance == null) Instance = this;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BindComponents();
    }
#endif

    public void BindComponents()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (startGameButton == null)
        {
            var t = FindChildRecursive(transform, "StartGameButton", "StartButton", "Start", "Play");
            if (t != null) startGameButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
        }
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
            startGameButton.onClick.AddListener(OnStartGameClicked);
        }

        if (closeButton == null)
        {
            var t = FindChildRecursive(transform, "CloseButton", "Exit", "Close", "X");
            if (t != null) closeButton = t.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
        }
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseClicked);
            closeButton.onClick.AddListener(OnCloseClicked);
        }

        if (titleText == null)
        {
            var t = FindChildRecursive(transform, "HeaderTitle", "Title");
            if (t != null) titleText = t.GetComponent<TextMeshProUGUI>();
        }

        if (guideText == null)
        {
            var t = FindChildRecursive(transform, "GuideContent", "Content", "Text (TMP)");
            if (t != null && (titleText == null || t != titleText.transform))
            {
                guideText = t.GetComponent<TextMeshProUGUI>();
            }
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
    /// Open Tutorial Popup
    /// </summary>
    public void ShowTutorial(Action onStartGame = null)
    {
        if (Instance == null) Instance = this;
        onStartGameCallback = onStartGame;

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(true);
        }

        BindComponents();

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeRoutine(1.0f, true));
    }

    /// <summary>
    /// Close Tutorial Popup
    /// </summary>
    public void HideTutorial()
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeRoutine(0.0f, false, () => {
            gameObject.SetActive(false);
        }));
    }

    private IEnumerator FadeRoutine(float targetAlpha, bool interactable, Action onComplete = null)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = interactable;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        canvasGroup.interactable = interactable;
        canvasGroup.blocksRaycasts = interactable;

        onComplete?.Invoke();
    }

    public void OnStartGameClicked()
    {
        Time.timeScale = 1.0f;
        if (onStartGameCallback != null)
        {
            onStartGameCallback.Invoke();
        }
        else
        {
            SceneManager.LoadScene("Ingame");
        }
    }

    public void OnCloseClicked()
    {
        HideTutorial();
    }
}