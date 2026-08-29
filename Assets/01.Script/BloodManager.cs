using System;
using UnityEngine;

public class BloodManager : MonoBehaviour
{
    public static BloodManager Instance { get; private set; }

    [Header("Blood Settings")]
    [SerializeField] private int maxTargetBlood = 100;
    private int currentTargetBlood;

    // 피를 짤 때 외부(UI, 모기 등)로 알리는 이벤트
    public event Action<int, int> OnBloodSucked; // (이번에 빤 양, 누적 빤 양)

    private int totalSuckedBlood = 0;
    public int TotalSuckedBlood => totalSuckedBlood;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        currentTargetBlood = maxTargetBlood;
    }

    /// <summary>
    /// 피를 빨 때 실제 빨아들인 양을 계산해 반환합니다.
    /// </summary>
    public int RequestSuckBlood(int requestedAmount)
    {
        if (currentTargetBlood <= 0) return 0;

        // 남은 피보다 많은 양을 요청할 경우 남은 양만큼만 차감 (Mathf.Min)
        int actualSucked = Mathf.Min(currentTargetBlood, requestedAmount);

        currentTargetBlood -= actualSucked;
        totalSuckedBlood += actualSucked;

        OnBloodSucked?.Invoke(actualSucked, totalSuckedBlood);

        Debug.Log($"[BloodManager] 흡혈: {actualSucked} | 남은 피: {currentTargetBlood} | 누적 피: {totalSuckedBlood}");
        return actualSucked;
    }
}