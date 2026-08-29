using System;
using UnityEngine;

/// <summary>
/// 흡혈량, 현재 잔여 혈액(에너지), 생존 시간 및 탈출(150ml)/최대만복(200ml) 상태를 전역 관리하는 싱글톤 매니저
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
                if (instance == null)
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

    // 내부 상태 변수
    private float currentBlood;
    private float totalSuckedBlood = 0f;
    private float gameStartTime = 0f;
    private float finalSurvivalTime = 0f;
    private bool isTrackingTime = true;
    private bool isEscapeTriggered = false;

    // 외부 공개 이벤트 (Decoupling)
    public event Action<float, float> OnBloodAmountChanged;
    public event Action<float, float> OnBloodSucked;
    public static event Action OnFullBelly;
    public static event Action OnBloodDepleted;

    // =========================================================================
    // [외부 공개 프로퍼티 - GameOverUI 및 MosquitoController 연동]
    // =========================================================================
    public float MaxTargetBlood => maxTargetBlood;
    public float EscapeThresholdBlood => escapeThresholdBlood;
    public float CurrentBlood => currentBlood;
    public float TotalSuckedBlood => totalSuckedBlood;

    // 생존 시간 연산: 추적 중일 때는 현재 시간과의 차이, 멈췄을 때는 최종 기록 반환
    public float SurvivalTime => isTrackingTime ? (Time.time - gameStartTime) : finalSurvivalTime;

    // 상태 체크 프로퍼티
    public bool IsEscapeReady => currentBlood >= escapeThresholdBlood;
    public bool IsFull => currentBlood >= maxTargetBlood;

    private void Awake()
    {
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

        ResetBlood();
    }

    private void Start()
    {
        gameStartTime = Time.time;
        isTrackingTime = true;

        // ⚠️ [기존 버그 수정] Start에서 currentBlood <= 0f 일 때 
        // 무조건 40f로 되돌려 버리던 삼항 연산자 강제 초기화 구문을 완전히 제거했습니다.
    }

    private void OnApplicationQuit()
    {
        isApplicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            isApplicationQuitting = true;
        }
    }

    // =========================================================================
    // [핵심 게임 로직 메서드]
    // =========================================================================

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
    /// 모기가 피를 빨 때 호출하여 혈액을 충전하고 누적치 기록
    /// </summary>
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
            Debug.LogWarning($"<color=green>[BloodManager] 탈출 기준({escapeThresholdBlood}ml) 달성! 탈출 시스템을 활성화합니다.</color>");
            OnFullBelly?.Invoke();
        }

        return actualSucked;
    }

    /// <summary>
    /// [F1 키 치트] 즉시 만복 상태로 변경
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
    /// 게임 오버 또는 클리어 시 생존 타이머를 멈추고 최종 기록 저장
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
    /// 혈액 수치 및 타이머 완벽 초기화
    /// </summary>
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