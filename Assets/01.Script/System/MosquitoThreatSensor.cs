using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 인디케이터에 표시될 3단계 위험도 정의
/// </summary>
public enum ThreatLevel
{
    Safe,    // 초록색 (01.png) - 위험도 낮음
    Warning, // 노란색 (02.png) - 위험도 중간
    Danger   // 빨간색 (03.png) - 위험도 높음
}

/// <summary>
/// [Unity 6 최신 규격 적용] 모기 주변의 위협도를 실시간 측정하는 센서 컴포넌트
/// </summary>
public class MosquitoThreatSensor : MonoBehaviour
{
    [Header("감지 레이어 및 반경")]
    [SerializeField] private LayerMask humanSkinLayer;
    [SerializeField] private float sensorRadius = 0.5f;

    [Header("성능 최적화 감지 주기 (초)")]
    [SerializeField] private float sampleInterval = 0.05f; // 1초에 20번 연산 (Update 남발 방지)

    [Header("거리 기반 감지 설정 (선택 사항)")]
    [SerializeField] private Transform headAnchor; // 머리/귀 중심점 Transform
    [SerializeField] private float maxDangerDistance = 2.0f; // 이 거리 이내로 들어오면 위험도 증가
    [SerializeField] private float minDangerDistance = 0.5f;

    // 위협 단계 변경 시 발생하는 이벤트 (현재 위협 단계, 0~1 위험도 비율)
    public event Action<ThreatLevel, float> OnThreatLevelChanged;

    private ThreatLevel currentThreatLevel = ThreatLevel.Safe;
    private float timer = 0f;

    // [최신 유니티 2D 물리 규격] ContactFilter2D 및 List 기반 GC Zero 버퍼
    private ContactFilter2D contactFilter;
    private readonly List<Collider2D> hitListBuffer = new List<Collider2D>(8);

    private void Awake()
    {
        // 1. 최신 2D 물리 필터 초기화
        contactFilter = new ContactFilter2D();
        contactFilter.SetLayerMask(humanSkinLayer);
        contactFilter.useLayerMask = true;
        contactFilter.useTriggers = true; // 피부 영역이 Trigger 콜라이더여도 정확히 감지!
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= sampleInterval)
        {
            timer = 0f;
            EvaluateCurrentThreat();
        }
    }

    /// <summary>
    /// 모기의 현재 위치 위험도를 연산 (Zero-GC 최신 물리 API 적용)
    /// </summary>
    private void EvaluateCurrentThreat()
    {
        float highestDangerRate = 0f;

        // 2. [Unity 6 규격] CS0618 경고가 해결된 OverlapCircle 오버로드 호출 (GC Allocation = 0B)
        int hitCount = Physics2D.OverlapCircle(transform.position, sensorRadius, contactFilter, hitListBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            if (hitListBuffer[i].TryGetComponent<IBodyPartZone>(out var zone))
            {
                if (zone.DangerProbability > highestDangerRate)
                {
                    highestDangerRate = zone.DangerProbability;
                }
            }
        }

        // 3. 머리 앵커(Head Anchor)와의 거리가 지정되어 있다면 거리 연산 추가 반영
        if (headAnchor != null)
        {
            float dist = Vector2.Distance(transform.position, headAnchor.position);
            // $D(d) = \text{Clamp01}\left(1 - \frac{d - d_{\min}}{d_{\max} - d_{\min}}\right)$
            float distanceDanger = Mathf.Clamp01(1f - ((dist - minDangerDistance) / (maxDangerDistance - minDangerDistance)));
            highestDangerRate = Mathf.Max(highestDangerRate, distanceDanger);
        }

        // 4. 위험도 비율을 3단계 ThreatLevel로 매핑
        ThreatLevel newLevel;
        if (highestDangerRate >= 0.7f)
        {
            newLevel = ThreatLevel.Danger;  // 빨강 (03.png)
        }
        else if (highestDangerRate >= 0.35f)
        {
            newLevel = ThreatLevel.Warning; // 노랑 (02.png)
        }
        else
        {
            newLevel = ThreatLevel.Safe;    // 초록 (01.png)
        }

        // 5. 상태가 변경되었을 때만 이벤트 발화 (성능 최적화)
        if (newLevel != currentThreatLevel)
        {
            currentThreatLevel = newLevel;
            OnThreatLevelChanged?.Invoke(currentThreatLevel, highestDangerRate);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, sensorRadius);
    }
}