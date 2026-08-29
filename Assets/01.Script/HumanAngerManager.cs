using System.Collections;
using UnityEngine;

/// <summary>
/// 인간의 분노 스택에 따라 공격 전조 시간을 5초부터 점진적으로 단축시키는 관리 매니저 클래스.
/// </summary>
public class HumanAngerManager : MonoBehaviour
{
    public static HumanAngerManager Instance { get; private set; }

    [Header("UI 에셋 및 캔버스 바인딩")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private GameObject handAttackUIPrefab;

    [Header("공격 속도 밸런싱 (Time Settings)")]
    [Tooltip("기본 분노 0일 때 공격 차오름 시간 (기본 5초)")]
    [SerializeField] private float baseFillDuration = 5.0f;

    [Tooltip("분노가 극에 달했을 때의 최소 공격 전조 시간 (유저 최후의 반응 속도)")]
    [SerializeField] private float minFillDuration = 0.5f;

    [Tooltip("손바닥 피격 판정 범위 반지름")]
    [SerializeField] private float attackRadius = 1.2f;

    [SerializeField] private LayerMask mosquitoLayer;

    [Header("인간 분노(Anger) 가속 설정")]
    [Tooltip("1회 회피할 때마다 추가되는 분노 계수 (0.25 = 회피할 때마다 25%씩 가속)")]
    [SerializeField] private float angerPerDodge = 0.25f;

    private int dodgeCount = 0;
    private float currentAngerMultiplier = 1.0f;

    /// <summary>
    /// 현재 인간의 분노 배율 (기본 1.0 ~ 회피 시 증가)
    /// </summary>
    public float CurrentAngerMultiplier => currentAngerMultiplier;

    private bool isAttacking = false;
    private Camera cachedMainCamera;

    private Camera MainCamera
    {
        get
        {
            if (cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main;
                if (cachedMainCamera == null)
                {
                    cachedMainCamera = Object.FindAnyObjectByType<Camera>();
                }
            }
            return cachedMainCamera;
        }
    }

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

        if (targetCanvas == null)
        {
            targetCanvas = Object.FindAnyObjectByType<Canvas>();
        }
    }

    /// <summary>
    /// 공격을 트리거합니다. 분노 수치에 따라 5초에서 점점 줄어드는 시간을 계산합니다.
    /// </summary>
    public void TriggerAttack(Vector2 targetWorldPosition)
    {
        if (isAttacking) return;

        if (handAttackUIPrefab == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] handAttackUIPrefab이 인스펙터에 할당되지 않았습니다!</color>");
            return;
        }

        StartCoroutine(HandAttackRoutine(targetWorldPosition));
    }

    /// <summary>
    /// 손바닥 공격 진행 코루틴 (고정 좌표 생성 및 연출 대기 후 데미지 처리)
    /// </summary>
    private IEnumerator HandAttackRoutine(Vector2 targetWorldPosition)
    {
        isAttacking = true;

        // T_fill = Max(T_min, T_base / AngerMultiplier)
        float currentFillDuration = Mathf.Max(minFillDuration, baseFillDuration / currentAngerMultiplier);

        Debug.Log($"<color=cyan>[공격 시작] 현재 분노 배율: {currentAngerMultiplier:F2}x | 경고 시간: {currentFillDuration:F2}초</color>");

        if (targetCanvas == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] targetCanvas를 찾을 수 없습니다!</color>");
            isAttacking = false;
            yield break;
        }

        // 1. UI Prefab 생성
        GameObject handInstance = Instantiate(handAttackUIPrefab, targetCanvas.transform);

        // 2. UI 제어기를 통한 위치 고정 초기화 및 연출 대기
        if (handInstance.TryGetComponent<HandAttackUIController>(out var uiController))
        {
            // [핵심] 생성된 UI에 고정시킬 월드 좌표(targetWorldPosition)와 Canvas 전달!
            uiController.Initialize(targetWorldPosition, targetCanvas);

            bool isAnimationFinished = false;
            uiController.StartHandCharge(currentFillDuration, () =>
            {
                isAnimationFinished = true;
            });

            // 연출이 완료될 때까지 대기
            yield return new WaitUntil(() => isAnimationFinished);
        }
        else
        {
            // Fallback: HandAttackUIController가 없을 경우 단순 대기
            yield return new WaitForSeconds(currentFillDuration);
        }

        // 3. 고정된 좌표 지점에 판정 실행
        ExecuteAttackDamage(targetWorldPosition);

        // 4. UI 제거 및 공격 상태 해제
        Destroy(handInstance);
        isAttacking = false;
    }

    /// <summary>
    /// 지정된 월드 좌표 범위 내에 모기가 있는지 판정하여 데미지를 입힙니다.
    /// </summary>
    private void ExecuteAttackDamage(Vector2 targetWorldPosition)
    {
        Collider2D hitMosquito = Physics2D.OverlapCircle(targetWorldPosition, attackRadius, mosquitoLayer);

        if (hitMosquito != null && hitMosquito.TryGetComponent<MosquitoController>(out var mosquito))
        {
            Debug.LogError("<color=red>[찰싹!] 손바닥 타격 성공! (모기 잡힘)</color>");
            mosquito.OnHitByHumanHand();
            ResetAnger();
        }
        else
        {
            OnAttackDodged();
        }
    }

    private void OnAttackDodged()
    {
        dodgeCount++;
        currentAngerMultiplier = 1.0f + (dodgeCount * angerPerDodge);

        float nextDuration = Mathf.Max(minFillDuration, baseFillDuration / currentAngerMultiplier);
        Debug.Log($"<color=yellow>[회피 성공!] 모기가 피했습니다! 분노 스택: {dodgeCount} | 다음 공격 시간: {nextDuration:F2}초</color>");
    }

    public void ResetAnger()
    {
        dodgeCount = 0;
        currentAngerMultiplier = 1.0f;
        Debug.Log("<color=green>[분노 초기화] 사람이 상쾌해졌습니다. 공격 시간이 5초로 리셋됩니다.</color>");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}