using UnityEngine;

/// <summary>
/// Player의 바라보는 방향에 맞춰 자식(Visual) 꼬리의 로컬 위치와 스프라이트 반전을 동기화하는 컴포넌트
/// </summary>

public class MosquitoTailSync : MonoBehaviour
{
    [Header("오프셋 설정")]
    [Tooltip("모기가 '오른쪽'을 바라볼 때 몸통 기준 꼬리의 로컬 위치 offset")]
    [SerializeField] private Vector2 rightFacingOffset = new Vector2(-0.5f, 0f);

    [Header("컴포넌트 캐싱")]
    [SerializeField] private SpriteRenderer tailSpriteRenderer;

    private Transform myTransform;

    private void Awake()
    {
        myTransform = transform;

        // SpriteRenderer 미할당 시 자동 캐싱 (GC 방지)
        if (tailSpriteRenderer == null)
        {
            tailSpriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

    /// <summary>
    /// Player의 방향 전환 시 호출되어 꼬리의 위치와 flipX를 동기화
    /// </summary>
    /// <param name="isFacingRight">오른쪽을 바라보고 있는지 여부</param>
    public void SynchronizeTail(bool isFacingRight)
    {
        if (myTransform == null) myTransform = transform;

        // 1. 방향 부호 산출 (+1: 오른쪽, -1: 왼쪽)
        float directionMultiplier = isFacingRight ? 1f : -1f;

        // 2. 로컬 좌표 계산: X 위치만 방향에 따라 반전
        Vector3 newLocalPosition = new Vector3(
            rightFacingOffset.x * directionMultiplier,
            rightFacingOffset.y,
            myTransform.localPosition.z
        );

        myTransform.localPosition = newLocalPosition;

        // 3. 꼬리 스프라이트 flipX 동기화
        if (tailSpriteRenderer != null)
        {
            tailSpriteRenderer.flipX = !isFacingRight; // 기본 꼬리가 왼쪽을 향하고 있다면 필요에 따라 조정
        }
    }

#if UNITY_EDITOR
    // [Visual Debugging] 인스펙터에서 오프셋 위치를 눈으로 쉽게 확인하기 위한 기즈모
    private void OnDrawGizmosSelected()
    {
        Vector3 parentPos = transform.parent != null ? transform.parent.position : transform.position;

        // 오른쪽 오프셋 위치 (청록색)
        Gizmos.color = Color.cyan;
        Vector3 rightPos = parentPos + (Vector3)rightFacingOffset;
        Gizmos.DrawWireSphere(rightPos, 0.08f);
        Gizmos.DrawLine(parentPos, rightPos);

        // 왼쪽 오프셋 위치 (빨간색)
        Gizmos.color = Color.red;
        Vector3 leftPos = parentPos + new Vector3(-rightFacingOffset.x, rightFacingOffset.y, 0f);
        Gizmos.DrawWireSphere(leftPos, 0.08f);
        Gizmos.DrawLine(parentPos, leftPos);
    }
#endif
}