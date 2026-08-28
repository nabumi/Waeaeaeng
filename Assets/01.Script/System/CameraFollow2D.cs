using UnityEngine;

/// <summary>
/// 2D 모기 플레이어를 부드럽게 추적하는 카메라 컨트롤러
/// </summary>
public class CameraFollow2D : MonoBehaviour
{
    [Header("추적 대상 설정")]
    [Tooltip("카메라가 추적할 모기 플레이어의 Transform을 할당하세요.")]
    [SerializeField] private Transform target;

    [Header("카메라 추적 속도 설정")]
    [Tooltip("목표 위치 도달 시간 (초 단위). 값이 작을수록 빠르게 따라붙습니다.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float smoothTime = 0.2f;

    [Header("카메라 오프셋 설정")]
    [Tooltip("2D 카메라 생존을 위해 Z축은 -10을 유지해야 합니다.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    // SmoothDamp 내부 속도 계산 변수 (GC 방지를 위해 필드 선언)
    private Vector3 currentVelocity = Vector3.zero;

    private void LateUpdate()
    {
        // 1. 추적 대상이 없는 예외 상황 방지 (플레이어 사망/파괴 시 안전장치)
        if (target == null) return;

        // 2. 모기의 현재 위치에 Z 오프셋(-10)을 더한 최종 목표 위치 산출
        Vector3 targetPosition = target.position + offset;

        // 3. SmoothDamp 연산을 통해 현재 위치에서 목표 위치로 부드럽게 보간 이동
        transform.position = Vector3.SmoothDamp(
            transform.position,     // 현재 카메라 위치
            targetPosition,         // 도달할 목표 위치
            ref currentVelocity,    // 현재 속도 참조 (내부 연산용)
            smoothTime              // 도달 시간
        );
    }

    #region 시각화 디버깅 (Visual Debugging)

    private void OnDrawGizmos()
    {
        // 에디터 상에서 카메라의 추적 목표 지점을 시각적으로 표시
        if (target != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(target.position + offset, 0.5f);
            Gizmos.DrawLine(transform.position, target.position + offset);
        }
    }

    #endregion
}