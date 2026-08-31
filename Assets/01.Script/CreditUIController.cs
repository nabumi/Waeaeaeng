using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // 💡 신규 Input System 네임스페이스 추가!

/// <summary>
/// 크레딧 팝업 UI의 연출, 입력 제어, 생명주기를 전담하는 컨트롤러
/// (Unity New Input System 완벽 지원)
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class CreditUIController : MonoBehaviour
{
    [Header("UI 컴포넌트 연결")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Button openButton;   // 크레딧 열기 버튼
    [SerializeField] private Button closeButton;  // 크레딧 닫기 버튼

    [Header("연출 설정")]
    [SerializeField] private float fadeDuration = 0.25f; // 페이드 인/아웃 시간(초)

    private Coroutine fadeCoroutine;
    private bool isOpen = false;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        SetUIState(0f, false);
    }

    private void OnEnable()
    {
        if (openButton != null) openButton.onClick.AddListener(OpenCredit);
        if (closeButton != null) closeButton.onClick.AddListener(CloseCredit);
    }

    private void OnDisable()
    {
        if (openButton != null) openButton.onClick.RemoveListener(OpenCredit);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseCredit);
    }

    private void Update()
    {
        // ---------------------------------------------------------
        // 💡 [New Input System 대응 코드]
        // ---------------------------------------------------------
        if (isOpen)
        {
            // 현재 연결된 키보드 장치가 존재하는지 Null Check 후 ESC 키 입력 검사
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseCredit();
            }
        }
    }

    public void OpenCredit()
    {
        if (isOpen) return;

        isOpen = true;
        StartFade(1f, true);
    }

    public void CloseCredit()
    {
        if (!isOpen) return;

        isOpen = false;
        StartFade(0f, false);
    }

    private void StartFade(float targetAlpha, bool blockRaycasts)
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha, blockRaycasts));
    }

    private IEnumerator FadeRoutine(float targetAlpha, bool blockRaycasts)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsedTime = 0f;

        canvasGroup.blocksRaycasts = blockRaycasts;
        canvasGroup.interactable = blockRaycasts;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsedTime / fadeDuration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothProgress);
            yield return null;
        }

        SetUIState(targetAlpha, blockRaycasts);
        fadeCoroutine = null;
    }

    private void SetUIState(float alpha, bool blockRaycasts)
    {
        canvasGroup.alpha = alpha;
        canvasGroup.blocksRaycasts = blockRaycasts;
        canvasGroup.interactable = blockRaycasts;
    }
}