using System;
using UnityEngine;

/// <summary>
/// 흡혈량, 현재 잔여 혈액(에너지), 생존 시간 및 탈출(150ml)/최대만복(200ml) 상태를 전역 관리하는 싱글톤 매니저
/// </summary>
public class BloodManager : MonoBehaviour
{
    private static BloodManager instance;
    private static bool isQuitting = false;

    public static BloodManager Instance
    {
        get
        {
            if (isQuitting) return null;
            if (instance == null)
            {
                instance = FindAnyObjectByType<BloodManager>();
                if (instance == null && Application.isPlaying)
                {
                    var go = new GameObject("[BloodManager]");
                    instance = go.AddComponent<BloodManager>();
                }
            }
            return instance;
        }
    }

    [Header("혈액 수치 설정")]
    [Tooltip("최대 저장 가능한 혈액량")]
    [SerializeField] private float maxTargetBlood = 200f;

    [Tooltip("탈출구가 열리는 기준 혈액량")]
    [SerializeField] private float escapeThresholdBlood = 150f;

    [Tooltip("게임 시작 시 지급되는 초기 혈액량")]
    [SerializeField] private float initialBlood = 40f;

    private float currentBlood;
    private float totalSuckedBlood = 0f;
    private float gameStartTime = 0f;
    private bool isTrackingTime = true;
    private bool isEscapeTriggered = false;

    // 이벤트: (현재 잔여 피, 최대 피)
    public event Action<float, float> OnBloodAmountChanged;
    // 이벤트: (이번에 빤 양, 누적 빤 양)
    public event Action<float, float> OnBloodSucked;
    // 이벤트: 탈출 가능 기준(150ml) 도달 이벤트
    public static event Action OnFullBelly;
    // 이벤트: 혈액 완전 고갈 (기아 사망 트리거용)
    public static event Action OnBloodDepleted;

    public float MaxTargetBlood => maxTargetBlood;
    public float EscapeThresholdBlood => escapeThresholdBlood;
    public float CurrentBlood => currentBlood;
    public float TotalSuckedBlood => totalSuckedBlood;
    public float SurvivalTime => isTrackingTime ? (Time.time - gameStartTime) : finalSurvivalTime;
    private float finalSurvivalTime = 0f;
    public bool IsEscapeReady => currentBlood >= escapeThresholdBlood;
    public bool IsFull => currentBlood >= maxTargetBlood;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        ResetBlood();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void Start()
    {
        gameStartTime = Time.time;
        isTrackingTime = true;
        if (currentBlood <= 0f)
        {
            currentBlood = initialBlood > 0f ? initialBlood : 40f;
        }
    }

    /// <summary>
    /// 혈액 소모 (비행 자연 소모, 대시 소모 등)
    /// </summary>
    public void ConsumeBlood(float amount)
    {
        if (currentBlood <= 0f) return;

        currentBlood = Mathf.Max(0f, currentBlood - amount);
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);

        if (currentBlood <= 0f)
        {
            Debug.LogWarning("<color=red>[BloodManager] 혈액이 완전히 고갈되었습니다! (기아 사망 트리거)</color>");
            OnBloodDepleted?.Invoke();
        }
    }

    /// <summary>
    /// 모기가 피를 빨 때 호출하여 혈액을 충전하고 누적치를 기록
    /// </summary>
    public float RequestSuckBlood(float requestedAmount)
    {
        if (currentBlood >= maxTargetBlood) return 0f;

        float actualSucked = Mathf.Min(maxTargetBlood - currentBlood, requestedAmount);
        currentBlood += actualSucked;
        totalSuckedBlood += actualSucked;

        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
        OnBloodSucked?.Invoke(actualSucked, totalSuckedBlood);

        // 150ml 돌파 시 탈출 시스템 트리거 (1회만 발화)
        if (currentBlood >= escapeThresholdBlood && !isEscapeTriggered)
        {
            isEscapeTriggered = true;
            Debug.LogWarning($"<color=green>[BloodManager] 탈출 기준({escapeThresholdBlood}ml) 달성! 탈출 시스템을 활성화합니다. (최대 {maxTargetBlood}ml까지 추가 흡혈 가능)</color>");
            OnFullBelly?.Invoke();
        }

        return actualSucked;
    }

    /// <summary>
    /// [치트] F1 키 입력 시 혈액을 즉시 최대치(200ml)로 충전하고 탈출구 개방
    /// </summary>
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

        Debug.LogWarning($"<color=cyan>[치트 활성화] F1 입력: 혈액량이 {maxTargetBlood}ml로 즉시 충전되었습니다!</color>");
    }

    /// <summary>
    /// 게임 종료 시 시간 측정 정지
    /// </summary>
    public void StopTimer()
    {
        if (isTrackingTime)
        {
            finalSurvivalTime = Time.time - gameStartTime;
            isTrackingTime = false;
        }
    }

    /// <summary>
    /// 혈액 및 통계 초기화
    /// </summary>
    public void ResetBlood()
    {
        currentBlood = initialBlood > 0f ? initialBlood : 40f;
        totalSuckedBlood = 0f;
        gameStartTime = Time.time;
        isTrackingTime = true;
        isEscapeTriggered = false;
        OnBloodAmountChanged?.Invoke(currentBlood, maxTargetBlood);
    }
}