using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 사망 시 1초 뒤 자연스럽게 페이드인되는 게임오버 결과창 UI 컨트롤러
/// </summary>
public class GameOverUIController : MonoBehaviour
{
    public static GameOverUIController Instance { get; private set; }

    [Header("페이드 연출 설정")]
    [SerializeField] private float fadeInDuration = 0.6f;

    [Header("UI 바인딩")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Button retryButton;

    private Coroutine fadeInCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // CanvasGroup 자동 구성
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

        // 재시작 버튼 자동 바인딩
        if (retryButton == null)
        {
            retryButton = GetComponentInChildren<Button>(true);
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(RestartGame);
            retryButton.onClick.AddListener(RestartGame);
        }
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

        // 자식 오브젝트 활성화 보장
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(true);
        }

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

    public void RestartGame()
    {
        Time.timeScale = 1.0f;
        string currentScene = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentScene);
    }
}
