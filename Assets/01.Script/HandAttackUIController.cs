using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손바닥 마스크 내부에서 장심(중앙)부터 피가 차오르는 연출과
/// 모기를 쫓아가지 않고 지정된 월드 좌표에 UI를 고정하는 완전체 UI 제어 클래스.
/// </summary>
public class HandAttackUIController : MonoBehaviour
{
    [Header("UI 레이어 바인딩")]
    [Tooltip("Mask_Container 하위에서 중앙 스케일이 커질 Red_Inner_Fill의 RectTransform")]
    [SerializeField] private RectTransform redInnerFillTransform;

    [Tooltip("최상단 선화 외곽선 이미지 (Hand_Outline_FG)")]
    [SerializeField] private Image outlineForegroundImage;

    [Header("연출 가속 및 피드백 설정")]
    [Tooltip("차오름 속도 가속 커브 (Ease-In 형태 권장)")]
    [SerializeField] private AnimationCurve fillEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("점멸 시작 지점 (0.85 = 85% 진행 시점부터 깜빡임)")]
    [Range(0.5f, 0.95f)]
    [SerializeField] private float flashStartThreshold = 0.85f;

    [Tooltip("외곽선 점멸 주파수(속도)")]
    [SerializeField] private float flashFrequency = 45f;

    // 위치 고정용 내부 변수
    private Vector2 targetWorldPosition;
    private Canvas parentCanvas;
    private Camera mainCamera;
    private bool isInitialized = false;
    private Coroutine chargeCoroutine;

    /// <summary>
    /// UI 생성 직후 HumanAngerManager에 의해 호출되어 고정 월드 좌표 및 캔버스를 설정합니다.
    /// </summary>
    public void Initialize(Vector2 worldPos, Canvas canvas)
    {
        this.targetWorldPosition = worldPos;
        this.parentCanvas = canvas;
        this.mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

        // 생성 직후 좌표 맞춤
        UpdateUIPosition();
        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized) return;

        // 모기가 이동하더라도 카메라 이동에 대응하여 월드 고정 좌표(targetWorldPosition)를 유지
        UpdateUIPosition();
    }

    /// <summary>
    /// 차오름 경고 연출을 시작하는 메인 함수
    /// </summary>
    public void StartHandCharge(float duration, Action onComplete)
    {
        if (chargeCoroutine != null)
            StopCoroutine(chargeCoroutine);

        chargeCoroutine = StartCoroutine(HandChargeRoutine(duration, onComplete));
    }

    private IEnumerator HandChargeRoutine(float duration, Action onComplete)
    {
        float timer = 0f;

        // 1. 초기화: 스케일을 0으로 설정하여 중앙점으로 축소
        if (redInnerFillTransform != null)
        {
            redInnerFillTransform.localScale = Vector3.zero;
        }

        // 2. 프레임 단위 차오름 및 연출 루프
        while (timer < duration)
        {
            timer += Time.deltaTime;

            // 선형 진행율 연산: $p = \min(1, \frac{t}{T})$
            float linearProgress = Mathf.Clamp01(timer / duration);

            // 커브/수학 이징 적용: $S(p) = \text{Easing}(p)$
            float easedProgress = fillEasing.Evaluate(linearProgress);

            // [핵심 연출 1] Pivot(0.5, 0.5) 기준으로 중앙에서 팽창!
            if (redInnerFillTransform != null)
            {
                redInnerFillTransform.localScale = new Vector3(easedProgress, easedProgress, 1f);
            }

            // [핵심 연출 2] 타격 직전 최상단 외곽선 붉은색 점멸(Flicker) 연출
            if (linearProgress >= flashStartThreshold && outlineForegroundImage != null)
            {
                // $\alpha = \sin(\omega \cdot t) \cdot 0.3 + 0.7$
                float flashAlpha = Mathf.Sin(Time.time * flashFrequency) * 0.3f + 0.7f;
                outlineForegroundImage.color = new Color(1f, 0f, 0f, flashAlpha);
            }

            yield return null;
        }

        // 3. 스케일 $1.0$ 보장
        if (redInnerFillTransform != null)
        {
            redInnerFillTransform.localScale = Vector3.one;
        }

        // 4. 차오름 완수 후 타격 실행 콜백 호출
        onComplete?.Invoke();
    }

    /// <summary>
    /// 월드 고정 좌표를 캔버스 렌더 모드에 맞게 변환 (유도탄 방지 핵심 메소드)
    /// </summary>
    private void UpdateUIPosition()
    {
        if (parentCanvas == null || mainCamera == null) return;

        RectTransform rectTransform = GetComponent<RectTransform>();

        if (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            rectTransform.position = mainCamera.WorldToScreenPoint(targetWorldPosition);
        }
        else if (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            Camera renderCam = parentCanvas.worldCamera != null ? parentCanvas.worldCamera : mainCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform,
                mainCamera.WorldToScreenPoint(targetWorldPosition),
                renderCam,
                out Vector2 localPoint
            );
            rectTransform.anchoredPosition = localPoint;
        }
        else // World Space Canvas
        {
            rectTransform.position = targetWorldPosition;
        }
    }

    private void OnDisable()
    {
        if (chargeCoroutine != null)
        {
            StopCoroutine(chargeCoroutine);
            chargeCoroutine = null;
        }
    }

    #region 시각적 디버깅 (Visual Debugging)

    [ContextMenu("Test Charge Animation (1.5s)")]
    private void TestChargeInEditor()
    {
        StartHandCharge(1.5f, () => Debug.Log("<color=red>[ECHO TD Test] 손바닥 차오름 완료! 찰싹!</color>"));
    }

    #endregion
}