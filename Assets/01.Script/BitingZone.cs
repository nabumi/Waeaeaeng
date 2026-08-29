using GlobalEnums;
using System.Collections.Generic;
using UnityEngine;

public class BitingZone : MonoBehaviour
{
    [Header("Zone Configuration")]
    [SerializeField] private ZoneType zoneType;
    [SerializeField] private GameObject biteMarkPrefab; // 모기 물린 자국 프레파브
    [SerializeField] private Sprite biteMarkSprite;    // 물린 자국 스프라이트 (모기자국2)

    [Header("Bite Mark Density Settings")]
    [Tooltip("모기 물린 자국의 물리적 반지름 (이 거리 이내에는 중복 흡혈 불가)")]
    [SerializeField] private float biteMarkRadius = 0.5f;

    // 이 부위에 생성된 모기 물린 자국들의 위치 리스트
    private readonly List<Vector3> biteMarkPositions = new List<Vector3>();

    public ZoneType CurrentZoneType
    {
        get => zoneType;
        set => zoneType = value;
    }

    private void Awake()
    {
        if (biteMarkSprite == null)
        {
            biteMarkSprite = Resources.Load<Sprite>("Sprites/모기자국2") ?? Resources.Load<Sprite>("Sprites/물린자국");
        }
    }

    /// <summary>
    /// 존 타입에 따른 1회 최대 흡혈량 반환 (플레이어가 충분히 빨 수 있도록 넉넉하게 설정)
    /// </summary>
    public float GetMaxSuckAmount()
    {
        return zoneType switch
        {
            ZoneType.Green => 35f,
            ZoneType.Yellow => 45f,
            ZoneType.Red => 60f,
            _ => 40f
        };
    }

    /// <summary>
    /// 착지하려는 좌표가 기존 물린 자국 반지름 이내에 존재하는지 검사
    /// </summary>
    public bool IsPositionAlreadyBitten(Vector3 checkPosition)
    {
        foreach (Vector3 markPos in biteMarkPositions)
        {
            float distance = Vector2.Distance((Vector2)checkPosition, (Vector2)markPos);
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
        else
        {
            if (biteMarkSprite == null)
            {
                biteMarkSprite = Resources.Load<Sprite>("Sprites/모기자국2") ?? Resources.Load<Sprite>("Sprites/물린자국");
            }

            if (biteMarkSprite != null)
            {
                GameObject mark = new GameObject("BiteMark");
                mark.transform.position = new Vector3(bitePosition.x, bitePosition.y, 0f);
                mark.transform.SetParent(transform);
                mark.transform.localScale = Vector3.one * 0.7f;
                mark.transform.Rotate(0f, 0f, Random.Range(0f, 360f));

                SpriteRenderer sr = mark.AddComponent<SpriteRenderer>();
                sr.sprite = biteMarkSprite;
                sr.sortingOrder = 5; // 피부 위에 렌더링
                sr.color = Color.white;
            }
        }

        Debug.Log($"<color=red>[BitingZone] '모기자국2' 생성 완료! 위치: {bitePosition} (총 자국 수: {biteMarkPositions.Count})</color>");
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