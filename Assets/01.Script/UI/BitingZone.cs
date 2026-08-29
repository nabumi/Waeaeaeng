using System.Collections.Generic;
using UnityEngine;
using GlobalEnums; // ZoneType 이넘 참조

public class BitingZone : MonoBehaviour
{
    [Header("Zone Configuration")]
    [SerializeField] private ZoneType zoneType;
    [SerializeField] private GameObject biteMarkPrefab; // 모기 물린 자국 프리팹

    // =========================================================================
    // [프로퍼티 수정] 외부(MosquitoController)에서 읽기 및 쓰기가 모두 가능하도록 set 추가
    // =========================================================================
    public ZoneType CurrentZoneType
    {
        get => zoneType;
        set => zoneType = value;
    }

    [Header("Bite Mark Density Settings")]
    [Tooltip("모기 물린 자국의 물리적 반지름 (이 거리 이내에는 중복 흡혈 불가)")]
    [SerializeField] private float biteMarkRadius = 0.5f;

    [Header("Bite Mark Rotation Settings")]
    [Tooltip("체크 시 아래 지정된 고정 각도로 자국이 생성됩니다. 해제 시 프리팹 기본 각도 사용.")]
    [SerializeField] private bool useCustomRotation = false;

    [Tooltip("자국 생성 시 적용할 Z축 회전 각도 (단위: 도)")]
    [SerializeField] private float fixedZRotation = 0f;

    // 이 부위에 생성된 모기 물린 자국들의 위치 리스트
    private readonly List<Vector3> biteMarkPositions = new List<Vector3>();

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

    public bool IsPositionAlreadyBitten(Vector3 checkPosition)
    {
        foreach (Vector3 markPos in biteMarkPositions)
        {
            float distance = Vector3.Distance(checkPosition, markPos);
            if (distance <= biteMarkRadius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 흡혈 완료 시 지정된 각도로 자국 프리팹을 생성합니다.
    /// </summary>
    public void RegisterBiteMark(Vector3 bitePosition)
    {
        biteMarkPositions.Add(bitePosition);

        if (biteMarkPrefab != null)
        {
            Quaternion spawnRotation = useCustomRotation
                ? Quaternion.Euler(0f, 0f, fixedZRotation)
                : biteMarkPrefab.transform.rotation;

            Instantiate(biteMarkPrefab, bitePosition, spawnRotation, transform);
        }

        Debug.Log($"[BitingZone] {zoneType} 존에 지정된 각도로 자국 생성 완료!");
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