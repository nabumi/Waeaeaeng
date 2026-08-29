using System;
using UnityEngine;

/// <summary>
/// 인게임 플레이 시간을 정밀 측정하고, 사망/클리어 시 즉시 시간을 고정하는 매니저
/// </summary>
public class PlayTimerManager : MonoBehaviour
{
    public static PlayTimerManager Instance { get; private set; }

    // 타이머 변경 시 UI에 전달되는 이벤트
    public static event Action<float> OnTimerUpdated;

    [Header("타이머 상태 설정")]
    [SerializeField] private bool autoStartOnAwake = true;

    /// <summary>
    /// 현재 누적된 순수 플레이 시간 (초 단위)
    /// </summary>
    public float ElapsedTime { get; private set; } = 0f;

    /// <summary>
    /// 타이머 동작 여부
    /// </summary>
    public bool IsRunning { get; private set; } = false;

    private int lastBroadcastSecond = -1;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        // [이벤트 연동] 승리(Clear) 이벤트 발생 시 자동 정지
        EscapeSystem.OnGameClear += StopTimerAndFreeze;

        // ※ 만약 프로젝트에 PlayerHealth/Death 이벤트가 있다면 아래처럼 연결할 수 있습니다.
        // PlayerHealth.OnPlayerDied += StopTimerAndFreeze;
    }

    private void OnDisable()
    {
        EscapeSystem.OnGameClear -= StopTimerAndFreeze;
        // PlayerHealth.OnPlayerDied -= StopTimerAndFreeze;
    }

    private void Start()
    {
        if (autoStartOnAwake)
        {
            StartTimer();
        }
    }

    private void Update()
    {
        // IsRunning이 false가 되는 순간 더 이상 ElapsedTime이 증가하지 않습니다!
        if (!IsRunning) return;

        ElapsedTime += Time.deltaTime;

        int currentSecond = Mathf.FloorToInt(ElapsedTime);
        if (currentSecond != lastBroadcastSecond)
        {
            lastBroadcastSecond = currentSecond;
            OnTimerUpdated?.Invoke(ElapsedTime);
        }
    }

    public void StartTimer()
    {
        IsRunning = true;
    }

    /// <summary>
    /// 타이머를 멈추고 최종 시간을 확정합니다.
    /// </summary>
    public void StopTimer()
    {
        if (!IsRunning) return; // 이미 멈췄다면 중복 실행 방지

        IsRunning = false;
        OnTimerUpdated?.Invoke(ElapsedTime); // 최종 고정 시간 전달
        Debug.Log($"<color=cyan>[PlayTimerManager]</color> 타이머 즉시 정지! 최종 측정 시간: {ElapsedTime:F2}초");
    }

    /// <summary>
    /// 타이머를 멈춤과 동시에 인게임 물리/시간 흐름을 모두 정지시킵니다.
    /// </summary>
    public void StopTimerAndFreeze()
    {
        StopTimer();
        Time.timeScale = 0f; // 인게임 모든 움직임 일시정지!
    }

    public void ResetTimer()
    {
        ElapsedTime = 0f;
        lastBroadcastSecond = -1;
        IsRunning = true;
        OnTimerUpdated?.Invoke(ElapsedTime);
    }
}