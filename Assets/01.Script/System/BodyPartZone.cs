using UnityEngine;

/// <summary>
/// 신체 부위 유형 정의
/// </summary>
public enum BodyPartType
{
    Head,       // 머리/귀 (위험도 최고)
    UpperBody,  // 상체/팔/어깨 (위험도 중간)
    LowerBody   // 하체/골반/발 (위험도 최저)
}

/// <summary>
/// 향후 분리 스프라이트 구조 변경에도 대응 가능한 인터페이스
/// </summary>
public interface IBodyPartZone
{
    BodyPartType PartType { get; }
    float DangerProbability { get; } // 0.0 ~ 1.0 (0% ~ 100%)
}

/// <summary>
/// 단일/분리 스프라이트 모두에 부착 가능한 부위 영역 트리를 위한 컴포넌트
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BodyPartZone : MonoBehaviour, IBodyPartZone
{
    [Header("부위 설정")]
    [SerializeField] private BodyPartType partType = BodyPartType.UpperBody;

    [Header("피격/들킬 위험 확률 (0.0 ~ 1.0)")]
    [Range(0f, 1f)]
    [SerializeField] private float dangerProbability = 0.5f;

    [Header("에디터 시각화 색상")]
    [SerializeField] private Color zoneGizmoColor = new Color(1f, 0.5f, 0f, 0.4f);

    public BodyPartType PartType => partType;
    public float DangerProbability => dangerProbability;

    private Collider2D zoneCollider;

    private void Awake()
    {
        zoneCollider = GetComponent<Collider2D>();
        // 안착 감지를 위해 반드시 Trigger 처리
        zoneCollider.isTrigger = true;
    }

    #region Visual Debugging

    private void OnDrawGizmos()
    {
        if (zoneCollider == null) zoneCollider = GetComponent<Collider2D>();
        if (zoneCollider == null) return;

        Gizmos.color = zoneGizmoColor;
        // 씬 뷰에서 부위별 영역을 투명 색상으로 시각화
        if (zoneCollider is CircleCollider2D circle)
        {
            Gizmos.DrawSphere((Vector2)transform.position + circle.offset, circle.radius);
        }
        else if (zoneCollider is BoxCollider2D box)
        {
            Gizmos.DrawCube((Vector2)transform.position + box.offset, box.size);
        }
    }

    #endregion
}
