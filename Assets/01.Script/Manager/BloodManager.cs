using System;
using UnityEngine;

/// <summary>
/// 흡혈량, 현재 잔여 혈액(에너지), 생존 시간 및 탈출 상태를 전역 관리하는 싱글톤 매니저
/// (씬 재시작 시 Time.timeScale 및 정적 이벤트 마비 현상을 완벽하게 방어합니다.)
/// </summary>
public class BloodManager : MonoBehaviour
{
    private static BloodManager instance;
    private static bool isApplicationQuitting = false;

    public static bool HasInstance => instance != null && !isApplicationQuitting;

    public static BloodManager Instance
    {
        get
        {
            if (isApplicationQuitting) return null;

            if (instance == null)
            {
                instance = FindAnyObjectByType<BloodManager>();
            }
            return instance;
        }
    }

    [Header("혈액 수치 설정")]
    [Tooltip("최대 저장 가능한 혈액량 (ml)")]
    [SerializeField] private float maxTargetBlood = 200f;

    [Tooltip("탈출구가 열리는 기준 혈액량 (ml)")]
    [SerializeField] private float escapeThresholdBlood = 150f;

    [Tooltip("게임 시작 시 지급되는 초기 혈액량 (ml)")]
    [SerializeField] private float initialBlood = 40f;

    // 내부 상태 변수
    private float currentBlood;
    private float totalSuckedBlood = 0f;
    private float gameStartTime = 0f;
    private float finalSurvivalTime = 0f;
    private bool isTrackingTime = true;
    private bool isEscapeTriggered = false;

    // 외부 공개 인스턴스 이벤트
    public event Action<float, float> OnBloodAmountChanged;
    public event Action<float, float> OnBloodSucked;

    // 정적 이벤트 (씬 재작동 시 찌꺼기 제거 필수)
    public static event Action OnFullBelly;
    public static event Action OnBloodDepleted;

    // =========================================================================
    // [외부 공개 프로퍼티]
    // =========================================================================
    public float MaxTargetBlood => maxTargetBlood;
    public float EscapeThresholdBlood => escapeThresholdBlood;
    public float CurrentBlood => currentBlood;
    public float TotalSuckedBlood => totalSuckedBlood;
    public float BloodRatio => maxTargetBlood > 0f ? Mathf.Clamp01(currentBlood / maxTargetBlood) : 0f;
    public float SurvivalTime => isTrackingTime ? (Time.time - gameStartTime) : finalSurvivalTime;
    public bool IsEscapeReady => currentBlood >= escapeThresholdBlood;
    public bool IsFull => currentBlood >= maxTargetBlood;

    private void Awake()
    {
        // 1. [핵심 해결책 1] 씬 재시작 시 정지되어 있던 시간을 즉시 1.0으로 강제 복구!
        if (Time.timeScale == 0f)
        {
            Time.timeScale = 1.0f;
            Debug.Log("<color=green>[BloodManager] Time.timeScale을 1.0f로 정상 복구했습니다.</color>");
        }

        isApplicationQuitting = false;

        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 2. [핵심 해결책 2] 씬 재시작 시 잔여 정적 이벤트 체인 초기화
        ClearStaticEvents();

        ResetBlood();
    }

    private void Start()
    {
        gameStartTime = Time.time;
        isTrackingTime = true;
        BroadcastCurrentState();
    }

    private void OnApplicationQuit()
    {
        isApplicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            ClearStaticEvents();
        }
    }

    /// <summary>
    /// 정적 이벤트에 남아있는 파괴된 객체들의 바인딩을 제거
    /// </summary>
    private static void ClearStaticEvents()
    {
        OnFullBelly = null;
        OnBloodDepleted = null;
    }

    // =========================================================================
    // [핵심 게임 로직 및 UI 동기화]
    // =========================================================================

    public void BroadcastCurrentState()
    {
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
    }

    /// <summary>
    /// 혈액 소모 공식: $V_{\text{current}} = \max(0, V_{\text{current}} - V_{\text{consume}})$
    /// </summary>
    public void ConsumeBlood(float amount)
    {
        if (currentBlood <= 0f) return;

        currentBlood = Mathf.Max(0f, currentBlood - amount);
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);

        if (currentBlood <= 0f)
        {
            Debug.LogWarning("<color=red>[BloodManager] 혈액 고갈! (기아 사망 트리거)</color>");
            OnBloodDepleted?.Invoke();
        }
    }

    public float RequestSuckBlood(float requestedAmount)
    {
        if (currentBlood >= maxTargetBlood) return 0f;

        float actualSucked = Mathf.Min(maxTargetBlood - currentBlood, requestedAmount);
        currentBlood += actualSucked;
        totalSuckedBlood += actualSucked;

        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
        OnBloodSucked?.Invoke(actualSucked, totalSuckedBlood);

        if (currentBlood >= escapeThresholdBlood && !isEscapeTriggered)
        {
            isEscapeTriggered = true;
            Debug.LogWarning($"<color=green>[BloodManager] 탈출 기준({escapeThresholdBlood}ml) 달성!</color>");
            OnFullBelly?.Invoke();
        }

        return actualSucked;
    }

    public void SetBloodFullCheat()
    {
        float added = maxTargetBlood - currentBlood;
        currentBlood = maxTargetBlood;
        totalSuckedBlood += Mathf.Max(0f, added);
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
        OnBloodSucked?.Invoke(added, totalSuckedBlood);

        if (!isEscapeTriggered)
        {
            isEscapeTriggered = true;
            OnFullBelly?.Invoke();
        }

        Debug.LogWarning($"<color=cyan>[치트 활성화] F1 입력: 혈액량이 {maxTargetBlood}ml로 충전되었습니다!</color>");
    }

    public void StopTimer()
    {
        if (isTrackingTime)
        {
            finalSurvivalTime = Time.time - gameStartTime;
            isTrackingTime = false;
        }
    }

    public void ResetBlood()
    {
        currentBlood = initialBlood > 0f ? initialBlood : 40f;
        totalSuckedBlood = 0f;
        gameStartTime = Time.time;
        finalSurvivalTime = 0f;
        isTrackingTime = true;
        isEscapeTriggered = false;
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
    }
}