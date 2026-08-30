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

    public static CameraFollow2D Instance { get; private set; }

    [Header("대시 시 다이내믹 줌(확대) 설정")]
    [Tooltip("대시 시 카메라 확대 배율 (0.85 = 기본 시야의 85%로 줌인/확대)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float dashZoomMultiplier = 0.85f;
    [Tooltip("줌 확대/복귀 보간 시간 (초)")]
    [SerializeField] private float zoomSmoothTime = 0.12f;

    private Camera cam;
    private float defaultOrthoSize = 5f;
    private float targetOrthoSize = 5f;
    private float zoomVelocity = 0f;
    private Vector3 currentVelocity = Vector3.zero;
    private Coroutine activeZoomCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        cam = GetComponent<Camera>() ?? Camera.main;
        if (cam != null)
        {
            defaultOrthoSize = cam.orthographicSize;
            targetOrthoSize = defaultOrthoSize;
        }
    }

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

        // 4. 카메라 줌인(확대) 보간
        if (cam != null)
        {
            cam.orthographicSize = Mathf.SmoothDamp(
                cam.orthographicSize,
                targetOrthoSize,
                ref zoomVelocity,
                zoomSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime
            );
        }
    }

    /// <summary>
    /// 대시 발동 시 호출되어 지정된 시간 동안 카메라를 역동적으로 확대(Zoom-in)합니다.
    /// </summary>
    /// <param name="duration">대시 지속 시간 (초)</param>
    public void TriggerDashZoom(float duration)
    {
        if (activeZoomCoroutine != null)
        {
            StopCoroutine(activeZoomCoroutine);
        }
        activeZoomCoroutine = StartCoroutine(DashZoomRoutine(duration));
    }

    private System.Collections.IEnumerator DashZoomRoutine(float duration)
    {
        // 1. 줌인 (확대)
        targetOrthoSize = defaultOrthoSize * dashZoomMultiplier;

        // 2. 대시 시간 동안 줌 유지
        yield return new WaitForSecondsRealtime(duration);

        // 3. 원래 시야로 부드럽게 복귀
        targetOrthoSize = defaultOrthoSize;
        activeZoomCoroutine = null;
    }

    /// <summary>
    /// 즉각 원래 시야로 리셋
    /// </summary>
    public void ResetZoomImmediate()
    {
        if (activeZoomCoroutine != null)
        {
            StopCoroutine(activeZoomCoroutine);
            activeZoomCoroutine = null;
        }
        targetOrthoSize = defaultOrthoSize;
        if (cam != null) cam.orthographicSize = defaultOrthoSize;
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