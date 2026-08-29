using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무작위 속도 변동과 피드백 텀이 포함된 무한 순환형 QTE 스킬체크 시스템.
/// 모기 사망 시 UI 잔상 방지를 위한 이벤트 기반 강제 중단 기능 포함.
/// </summary>
public class SkillCheckUI : MonoBehaviour
{
    public static SkillCheckUI Instance { get; private set; }

    [Header("UI 요소 바인딩")]
    [SerializeField] private GameObject uiContainer;          // 전체 UI 패널
    [SerializeField] private Image successZoneImage;          // 일반 성공 띠 (Radial 360)
    [SerializeField] private Image greatZoneImage;            // 대성공 띠 (Radial 360)
    [SerializeField] private RectTransform markerRectTransform;// 띠 트랙 위를 타고 도는 마커

    [Header("궤도 및 회전 설정")]
    [SerializeField] private float trackRadius = 100f;         // 띠 트랙의 반지름 ($R$, 픽셀 단위)
    [SerializeField] private float baseRotationSpeed = 240f;   // 기본 회전 속도 ($\omega_{\text{base}}$, 도/초)

    [Header("속도 무작위성 (Randomization) 설정")]
    [Range(0.5f, 2.0f)]
    [SerializeField] private float minSpeedMultiplier = 0.8f;  // 최소 무작위 속도 배율
    [Range(1.0f, 3.0f)]
    [SerializeField] private float maxSpeedMultiplier = 1.6f;  // 최대 무작위 속도 배율

    [Header("영역 크기 설정")]
    [SerializeField] private float successArcDeg = 45f;        // 일반 성공 영역 각도 ($\theta_{\text{success}}$)
    [SerializeField] private float greatArcDeg = 10f;          // 대성공 영역 각도 ($\theta_{\text{great}}$)

    [Header("연출 텀 설정 (탁, 탁 리듬감)")]
    [SerializeField] private float feedbackDelay = 0.25f;      // 버튼 입력 후 결과 처리까지의 대기 시간 ($\Delta t_{\text{delay}}$)

    private bool isActive = false;
    private bool isEvaluating = false;
    private float currentSpeed;
    private float currentNeedleAngle; // 현재 각도 ($0^\circ \sim 360^\circ$)

    // 판정용 각도 보존 변수
    private float successStartAngle;
    private float successEndAngle;
    private float greatStartAngle;
    private float greatEndAngle;

    private Action<SkillCheckResult> onCompleteCallback;
    private Coroutine feedbackCoroutine; // 피드백 대기 코루틴 추적용 변수

    public enum SkillCheckResult
    {
        Fail,           // 실패
        Success,        // 일반 성공
        GreatSuccess    // 대성공
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (uiContainer != null) uiContainer.SetActive(false);
    }

    private void OnEnable()
    {
        // [수정 완료] 최신화된 MosquitoController.OnMosquitoDied 이벤트를 구독
        MosquitoController.OnMosquitoDied += ForceCancelSkillCheck;
    }

    private void OnDisable()
    {
        // [수정 완료] 메모리 누수 방지를 위한 이벤트 구독 해제
        MosquitoController.OnMosquitoDied -= ForceCancelSkillCheck;
    }

    /// <summary>
    /// 스킬체크 시퀀스 시작 (매번 무작위 속도 적용)
    /// </summary>
    /// <param name="externalSpeedMultiplier">외부(위협도 등)에서 넘어오는 추가 배율</param>
    /// <param name="onComplete">결과 콜백</param>
    public void BeginSkillCheck(float externalSpeedMultiplier, Action<SkillCheckResult> onComplete)
    {
        // 만약 이전 피드백 코루틴이 돌고 있다면 안전하게 정지
        if (feedbackCoroutine != null)
        {
            StopCoroutine(feedbackCoroutine);
            feedbackCoroutine = null;
        }

        onCompleteCallback = onComplete;
        isEvaluating = false;

        // 최소~최대 범위 내에서 무작위 배율 추출
        float randomSpeedFactor = UnityEngine.Random.Range(minSpeedMultiplier, maxSpeedMultiplier);
        currentSpeed = baseRotationSpeed * externalSpeedMultiplier * randomSpeedFactor;

        Debug.Log($"<color=cyan>[스킬체크 생성] 이번 회전 속도 배율: {randomSpeedFactor:F2} (실제 속도: {currentSpeed:F1}°/s)</color>");

        // 1. 성공 시작 위치 랜덤 배치 ($60^\circ \sim 300^\circ$)
        successStartAngle = UnityEngine.Random.Range(60f, 360f - successArcDeg);
        successEndAngle = successStartAngle + successArcDeg;

        // 2. 대성공 영역은 성공 영역 끝에 배치
        greatEndAngle = successEndAngle;
        greatStartAngle = successEndAngle - greatArcDeg;

        // 3. UI Image Fill Amount 및 회전 세팅
        if (successZoneImage != null)
        {
            successZoneImage.fillAmount = successArcDeg / 360f;
            successZoneImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -successStartAngle);
        }

        if (greatZoneImage != null)
        {
            greatZoneImage.fillAmount = greatArcDeg / 360f;
            greatZoneImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -greatStartAngle);
        }

        // 4. 마커 초기화 ($0^\circ$ 상단에서 시작)
        currentNeedleAngle = 0f;
        UpdateMarkerPosition(currentNeedleAngle);

        isActive = true;
        if (uiContainer != null) uiContainer.SetActive(true);
    }

    private void Update()
    {
        if (!isActive || isEvaluating) return;

        // 시간 흐름에 따른 각도 증가 공식: $\theta(t) = \theta_0 + \omega \cdot \Delta t$
        currentNeedleAngle += currentSpeed * Time.deltaTime;

        if (currentNeedleAngle >= 360f)
        {
            currentNeedleAngle -= 360f;
        }

        UpdateMarkerPosition(currentNeedleAngle);
    }

    private void UpdateMarkerPosition(float angleDeg)
    {
        if (markerRectTransform == null) return;

        // 삼각함수를 이용한 원형 궤도 좌표 산출: $x = R \cdot \sin(\theta), y = R \cdot \cos(\theta)$
        float rad = angleDeg * Mathf.Deg2Rad;
        float x = trackRadius * Mathf.Sin(rad);
        float y = trackRadius * Mathf.Cos(rad);

        markerRectTransform.anchoredPosition = new Vector2(x, y);
        markerRectTransform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
    }

    /// <summary>
    /// 플레이어 입력 판정
    /// </summary>
    public void OnInputPressed()
    {
        if (!isActive || isEvaluating) return;

        isEvaluating = true;
        isActive = false;

        SkillCheckResult result;

        // 1. 대성공 범위 판정
        if (currentNeedleAngle >= greatStartAngle && currentNeedleAngle <= greatEndAngle)
        {
            result = SkillCheckResult.GreatSuccess;
        }
        // 2. 일반 성공 범위 판정
        else if (currentNeedleAngle >= successStartAngle && currentNeedleAngle <= successEndAngle)
        {
            result = SkillCheckResult.Success;
        }
        // 3. 실패
        else
        {
            result = SkillCheckResult.Fail;
        }

        // 피드백 대기 코루틴 참조 보존
        feedbackCoroutine = StartCoroutine(ProcessFeedbackRoutine(result));
    }

    private IEnumerator ProcessFeedbackRoutine(SkillCheckResult result)
    {
        yield return new WaitForSecondsRealtime(feedbackDelay);

        if (uiContainer != null) uiContainer.SetActive(false);

        feedbackCoroutine = null;
        onCompleteCallback?.Invoke(result);
    }

    /// <summary>
    /// 모기 사망(게임오버) 시 스킬체크를 즉시 강제 중단하고 UI 잔상을 제거합니다.
    /// </summary>
    public void ForceCancelSkillCheck()
    {
        Debug.Log("<color=orange>[SkillCheckUI] 사망 신호 감지: 스킬체크 강제 중단 및 UI 비활성화</color>");

        // 1. 플래그 즉시 차단
        isActive = false;
        isEvaluating = false;

        // 2. 진행 중인 피드백 대기 텀 코루틴이 있다면 강제 정지
        if (feedbackCoroutine != null)
        {
            StopCoroutine(feedbackCoroutine);
            feedbackCoroutine = null;
        }

        // 3. UI 컨테이너 즉시 숨김 처리 (잔상 방지)
        if (uiContainer != null)
        {
            uiContainer.SetActive(false);
        }

        // 4. 꼬인 콜백 참조 해제 (사망 후 콜백 실행 방지)
        onCompleteCallback = null;
    }
}