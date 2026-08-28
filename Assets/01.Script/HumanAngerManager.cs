using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손바닥 UI 연출 및 좌표 변환 시 발생할 수 있는 NullReference를 100% 차단한 매니저
/// </summary>
public class HumanAngerManager : MonoBehaviour
{
    public static HumanAngerManager Instance { get; private set; }

    [Header("UI 에셋 및 캔버스 바인딩")]
    [SerializeField] private Canvas targetCanvas;           // 사용 중인 UI Canvas 참조
    [SerializeField] private GameObject handAttackUIPrefab; // 손바닥 UI 프리팹

    [Header("공격 속도 및 판정 범위")]
    [SerializeField] private float baseFillDuration = 1.2f;
    [SerializeField] private float minFillDuration = 0.35f;
    [SerializeField] private float attackRadius = 1.2f;
    [SerializeField] private LayerMask mosquitoLayer;

    [Header("인간 분노(Anger) 설정")]
    [SerializeField] private float angerPerDodge = 0.25f;

    private int dodgeCount = 0;
    private float currentAngerMultiplier = 1f;
    private bool isAttacking = false;

    // 카메라 캐싱 변수
    private Camera cachedMainCamera;

    // [핵심] 널 체크를 보장하는 프라퍼티 (Lazy Property)
    private Camera MainCamera
    {
        get
        {
            if (cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main; // 1순위: Tag가 MainCamera인 카메라

                if (cachedMainCamera == null)
                {
                    // 2순위: 씬 내의 아무 카메라나 탐색 (최신 API)
                    cachedMainCamera = Object.FindAnyObjectByType<Camera>();
                }
            }
            return cachedMainCamera;
        }
    }

    public float CurrentAngerMultiplier => currentAngerMultiplier;

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

        // Canvas 자동 탐색 (최신 유니티 6/2023+ API)
        if (targetCanvas == null)
        {
            targetCanvas = Object.FindAnyObjectByType<Canvas>();
        }
    }

    public void TriggerAttack(Vector2 targetWorldPosition)
    {
        if (isAttacking) return;

        // [방어 코드 1] 손바닥 UI 프리팹 할당 검증
        if (handAttackUIPrefab == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] handAttackUIPrefab이 인스펙터에 할당되지 않았습니다!</color>");
            return;
        }

        StartCoroutine(HandAttackRoutine(targetWorldPosition));
    }

    private IEnumerator HandAttackRoutine(Vector2 targetWorldPosition)
    {
        isAttacking = true;

        float fillDuration = Mathf.Max(minFillDuration, baseFillDuration / currentAngerMultiplier);

        // [방어 코드 2] targetCanvas 검증
        if (targetCanvas == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] targetCanvas를 찾을 수 없습니다!</color>");
            isAttacking = false;
            yield break;
        }

        // 1. UI 프리팹 생성
        GameObject handInstance = Instantiate(handAttackUIPrefab, targetCanvas.transform);
        RectTransform handRectTransform = handInstance.GetComponent<RectTransform>();

        // [방어 코드 3] RectTransform 유효성 확인
        if (handRectTransform == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] UI 프리팹에 RectTransform 컴포넌트가 없습니다!</color>");
            Destroy(handInstance);
            isAttacking = false;
            yield break;
        }

        // 2. 렌더 모드별 예외 처리된 안전한 좌표 변환 실행
        SetUIPositionByCanvasMode(handRectTransform, targetWorldPosition);

        Image fillImage = handInstance.GetComponentInChildren<Image>();
        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillAmount = 0f;
            fillImage.color = Color.red;
        }

        // 3. Red Fill 차오름 연출
        float timer = 0f;
        while (timer < fillDuration)
        {
            timer += Time.deltaTime;

            if (fillImage != null)
            {
                fillImage.fillAmount = Mathf.Clamp01(timer / fillDuration);
            }
            yield return null;
        }

        // 4. 타격 판정 연산
        Collider2D hitMosquito = Physics2D.OverlapCircle(targetWorldPosition, attackRadius, mosquitoLayer);

        if (hitMosquito != null && hitMosquito.TryGetComponent<MosquitoController>(out var mosquito))
        {
            Debug.LogError("<color=red>[찰싹!] 손바닥 타격 성공!</color>");
            mosquito.OnHitByHumanHand();
        }
        else
        {
            OnAttackDodged();
        }

        Destroy(handInstance);
        isAttacking = false;
    }

    /// <summary>
    /// 모든 Null 예외를 방어하도록 보완된 좌표계 변환 함수 (기존 137번째 줄 버그 수정)
    /// </summary>
    private void SetUIPositionByCanvasMode(RectTransform uiRect, Vector2 worldPos)
    {
        if (uiRect == null || targetCanvas == null) return;

        // [핵심 방어] 안전한 카메라 호출
        Camera cam = MainCamera;
        if (cam == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] 씬에 활성화된 카메라가 없어 UI 위치를 맞출 수 없습니다!</color>");
            return;
        }

        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // $\vec{P}_{\text{screen}} = \text{WorldToScreenPoint}(\vec{P}_{\text{world}})$
            Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);
            uiRect.position = screenPoint;
        }
        else if (targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            Camera renderCam = targetCanvas.worldCamera != null ? targetCanvas.worldCamera : cam;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetCanvas.transform as RectTransform,
                cam.WorldToScreenPoint(worldPos),
                renderCam,
                out Vector2 localPoint
            );
            uiRect.anchoredPosition = localPoint;
        }
        else // RenderMode.WorldSpace
        {
            uiRect.position = worldPos;
        }
    }

    private void OnAttackDodged()
    {
        dodgeCount++;
        currentAngerMultiplier = 1.0f + (dodgeCount * angerPerDodge);
        Debug.Log($"<color=yellow>[회피!] 사람이 더 화났습니다! 배율: {currentAngerMultiplier:F2}x</color>");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}