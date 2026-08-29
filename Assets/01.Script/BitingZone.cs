using GlobalEnums;
using System.Collections.Generic;
using UnityEngine;

public class BitingZone : MonoBehaviour
{
    [Header("Zone Configuration")]
    [SerializeField] private ZoneType zoneType;
    [SerializeField] private GameObject biteMarkPrefab; // 모기 물린 자국 프레파브

    [Header("Bite Mark Density Settings")]
    [Tooltip("모기 물린 자국의 물리적 반지름 (이 거리 이내에는 중복 흡혈 불가)")]
    [SerializeField] private float biteMarkRadius = 0.5f;

    // 이 부위에 생성된 모기 물린 자국들의 위치 리스트
    private readonly List<Vector3> biteMarkPositions = new List<Vector3>();

    public ZoneType CurrentZoneType => zoneType;

    /// <summary>
    /// 존 타입에 따른 1회 최대 흡혈량 반환
    /// </summary>
    public int GetMaxSuckAmount()
    {
        return zoneType switch
        {
            ZoneType.Green => 10,
            ZoneType.Yellow => 15,
            ZoneType.Red => 20,
            _ => 0
        };
    }

    /// <summary>
    /// 착지하려는 좌표가 기존 물린 자국 반지름 이내에 존재하는지 검사
    /// </summary>
    public bool IsPositionAlreadyBitten(Vector3 checkPosition)
    {
        foreach (Vector3 markPos in biteMarkPositions)
        {
            float distance = Vector3.Distance(checkPosition, markPos);
            if (distance <= biteMarkRadius)
            {
                return true; // 이미 물린 영역 내 착지 시도
            }
        }
        return false; // 빨 수 있는 깨끗한 피부
    }

    /// <summary>
    /// 흡혈 완료 시 해당 위치에 자국 생성 및 좌표 등록
    /// </summary>
    public void RegisterBiteMark(Vector3 bitePosition)
    {
        biteMarkPositions.Add(bitePosition);

        if (biteMarkPrefab != null)
        {
            GameObject mark = Instantiate(biteMarkPrefab, bitePosition, Quaternion.identity, transform);
            mark.transform.Rotate(0f, 0f, Random.Range(0f, 360f));
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (Vector3 pos in biteMarkPositions)
        {
            Gizmos.DrawWireSphere(pos, biteMarkRadius);
        }
    }
#endif
}