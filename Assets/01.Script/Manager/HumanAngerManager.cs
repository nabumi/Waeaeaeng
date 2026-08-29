using System.Collections;
using UnityEngine;

/// <summary>
/// 인간의 분노 스택 관리 및 손바닥 공격 시퀀스를 제어하는 싱글톤 매니저 클래스.
/// </summary>
public class HumanAngerManager : MonoBehaviour
{
    public static HumanAngerManager Instance { get; private set; }

    [Header("UI 에셋 및 캔버스 바인딩")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private GameObject handAttackUIPrefab;

    [Header("손바닥 회전 연출 설정")]
    [Tooltip("손바닥 UI가 회전할 최소 Z 각도")]
    [SerializeField] private float minRotationAngle = -60f;

    [Tooltip("손바닥 UI가 회전할 최대 Z 각도")]
    [SerializeField] private float maxRotationAngle = 60f;

    [Header("공격 속도 밸런싱 (Time Settings)")]
    [Tooltip("기본 분노 0일 때 공격 차오름 시간 (기본 5초)")]
    [SerializeField] private float baseFillDuration = 5.0f;

    [Tooltip("최소 공격 전조 시간")]
    [SerializeField] private float minFillDuration = 0.5f;

    [Tooltip("손바닥 피격 판정 범위 반지름")]
    [SerializeField] private float attackRadius = 1.2f;

    [SerializeField] private LayerMask mosquitoLayer;

    [Header("인간 분노(Anger) 가속 설정")]
    [Tooltip("1회 회피할 때마다 추가되는 분노 계수")]
    [SerializeField] private float angerPerDodge = 0.25f;

    private int dodgeCount = 0;
    private float currentAngerMultiplier = 1.0f;

    // [핵심 해결] MosquitoController가 외부에서 읽을 수 있도록 public 프로퍼티 명시!
    /// <summary>
    /// 현재 인간의 분노 배율 ($A_{\text{mult}} \ge 1.0$)
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

    private IEnumerator HandAttackRoutine(Vector2 targetWorldPosition)
    {
        isAttacking = true;

        // 경고 시간 계산: $T_{\text{fill}} = \max\left(T_{\min}, \frac{T_{\text{base}}}{A_{\text{mult}}}\right)$
        float currentFillDuration = Mathf.Max(minFillDuration, baseFillDuration / currentAngerMultiplier);

        if (targetCanvas == null)
        {
            Debug.LogError("<color=red>[HumanAngerManager] targetCanvas를 찾을 수 없습니다!</color>");
            isAttacking = false;
            yield break;
        }

        GameObject handInstance = Instantiate(handAttackUIPrefab, targetCanvas.transform);
        float randomAngle = Random.Range(minRotationAngle, maxRotationAngle);

        if (handInstance.TryGetComponent<HandAttackUIController>(out var uiController))
        {
            uiController.Initialize(targetWorldPosition, targetCanvas, randomAngle);

            bool isSequenceFinished = false;

            // 시퀀스 호출: 손바닥 강타 순간 데미지 판정, 전체 완료 후 삭제
            uiController.StartHandAttackSequence(
                currentFillDuration,
                onHitImpact: () =>
                {
                    ExecuteAttackDamage(targetWorldPosition);
                },
                onSequenceComplete: () =>
                {
                    isSequenceFinished = true;
                }
            );

            yield return new WaitUntil(() => isSequenceFinished);
        }
        else
        {
            yield return new WaitForSeconds(currentFillDuration);
            ExecuteAttackDamage(targetWorldPosition);
        }

        Destroy(handInstance);
        isAttacking = false;
    }

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
        Debug.Log($"<color=yellow>[회피 성공!] 분노 스택: {dodgeCount} | 배율: {currentAngerMultiplier:F2}x</color>");
    }

    public void ResetAnger()
    {
        dodgeCount = 0;
        currentAngerMultiplier = 1.0f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}