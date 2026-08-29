using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설명서(튜토리얼 팝업) 컨트롤러
/// - 고화질 그래픽 조작 가이드 이미지 표시
/// - 화면 어디든 클릭하거나 아무 키나 누르면 즉시 게임 플레이(인게임)로 전환
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TutorialPopupController : MonoBehaviour
{
    public static TutorialPopupController Instance { get; private set; }

    [Header("Fade Settings")]
    [SerializeField] private float fadeInDuration = 0.25f;

    [Header("UI Component Bindings")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image guideImage;
    [SerializeField] private Sprite guideSprite;

    private Coroutine fadeCoroutine;
    private Action onStartGameCallback;
    private bool isPopupOpen = false;
    private float openTimestamp = 0f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        EnsureCanvasGroup();
        EnsureGuideVisual();
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
    }

    private void EnsureGuideVisual()
    {
        if (guideSprite == null)
        {
            guideSprite = Resources.Load<Sprite>("ui/tutorial_guide");
        }

        if (guideImage == null)
        {
            var contentPanel = FindChildRecursive(transform, "ContentPanel", "Guide", "Panel", "Image");
            if (contentPanel != null)
            {
                guideImage = contentPanel.GetComponent<Image>();
            }
            if (guideImage == null)
            {
                guideImage = GetComponentInChildren<Image>();
            }
        }

        if (guideImage != null)
        {
            if (guideSprite != null)
            {
                guideImage.sprite = guideSprite;
            }
            guideImage.color = Color.white;
            guideImage.preserveAspect = true;
        }
    }

    private void Start()
    {
        if (Instance == null) Instance = this;
        EnsureCanvasGroup();
        EnsureGuideVisual();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        isPopupOpen = false;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!isPopupOpen) return;

        // 팝업 열린 직후 0.08초 동안은 오입력 방지
        if (Time.unscaledTime - openTimestamp < 0.08f) return;

        // 마우스 클릭, 터치, 또는 아무 키 입력 감지 시 게임 시작
        bool isClicked = (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) ||
                         (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) ||
                         (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (isClicked)
        {
            OnStartGameClicked();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BindComponents();
    }
#endif

    public void BindComponents()
    {
        EnsureCanvasGroup();
        EnsureGuideVisual();

        // 1. 기존 텍스트 및 서브 버튼들 숨김 (이미지 자체에 모든 가이드가 깔끔하게 포함됨)
        var oldTitle = FindChildRecursive(transform, "HeaderTitle", "Title");
        if (oldTitle != null) oldTitle.gameObject.SetActive(false);

        var oldGuide = FindChildRecursive(transform, "GuideContent", "Content");
        if (oldGuide != null) oldGuide.gameObject.SetActive(false);

        var oldCloseBtn = FindChildRecursive(transform, "CloseButton", "Exit", "Close");
        if (oldCloseBtn != null) oldCloseBtn.gameObject.SetActive(false);

        var oldStartBtn = FindChildRecursive(transform, "StartGameButton", "StartButton");
        if (oldStartBtn != null) oldStartBtn.gameObject.SetActive(false);

        // 2. 딤 배경 및 패널에 클릭 버튼 바인딩 (화면 전체 어디를 눌러도 게임 시작)
        var dimBg = FindChildRecursive(transform, "DimBackground", "Background", "Dim");
        if (dimBg != null)
        {
            if (!dimBg.TryGetComponent<Button>(out var bgBtn))
            {
                bgBtn = dimBg.gameObject.AddComponent<Button>();
            }
            bgBtn.onClick.RemoveListener(OnStartGameClicked);
            bgBtn.onClick.AddListener(OnStartGameClicked);
        }

        var contentPanel = FindChildRecursive(transform, "ContentPanel");
        if (contentPanel != null)
        {
            if (!contentPanel.TryGetComponent<Button>(out var panelBtn))
            {
                panelBtn = contentPanel.gameObject.AddComponent<Button>();
            }
            panelBtn.onClick.RemoveListener(OnStartGameClicked);
            panelBtn.onClick.AddListener(OnStartGameClicked);
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

        EnsureGuideVisual();
        BindComponents();

        isPopupOpen = true;
        openTimestamp = Time.unscaledTime;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeRoutine(1.0f, true));
    }

    /// <summary>
    /// Close Tutorial Popup
    /// </summary>
    public void HideTutorial()
    {
        isPopupOpen = false;
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeRoutine(0.0f, false, () => {
            gameObject.SetActive(false);
        }));
    }

    private IEnumerator FadeRoutine(float targetAlpha, bool interactable, Action onComplete = null)
    {
        EnsureCanvasGroup();

        if (canvasGroup != null)
        {
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
        }

        onComplete?.Invoke();
    }

    public void OnStartGameClicked()
    {
        if (!isPopupOpen && !gameObject.activeInHierarchy) return;
        isPopupOpen = false;

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
        OnStartGameClicked();
    }
}