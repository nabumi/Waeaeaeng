using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 사람의 분노 단계 관리 및 손바닥 UI 차오름 공격 연출을 총괄하는 시니어 TD 매니저
/// </summary>
public class HumanAngerManager : MonoBehaviour
{
    public static HumanAngerManager Instance { get; private set; }

    [Header("UI 에셋 바인딩")]
    [SerializeField] private Canvas worldCanvas;            // World Space 캔버스 참조
    [SerializeField] private GameObject handAttackUIPrefab; // 손바닥 모양 UI 프리팹 (Image Filled 타입)

    [Header("공격 속도 및 판정 범위")]
    [SerializeField] private float baseFillDuration = 1.2f;  // 기본 손바닥 차오르는 시간 ($T_{\text{base}}$, 초)
    [SerializeField] private float minFillDuration = 0.35f;  // 최대로 화났을 때 최소 차오르는 시간 ($T_{\min}$, 초)
    [SerializeField] private float attackRadius = 1.2f;      // 손바닥 피격 범위 ($r_{\text{slap}}$, 미터)
    [SerializeField] private LayerMask mosquitoLayer;        // 모기 레이어

    [Header("인간 분노(Anger) 시스템 설정")]
    [SerializeField] private float angerPerDodge = 0.25f;    // 회피 1회당 분노 증가량 ($\Delta_{\text{anger}}$)

    private int dodgeCount = 0;               // 성공한 회피 횟수
    private float currentAngerMultiplier = 1f; // 현재 분노 배율 ($M_{\text{anger}}$)
    private bool isAttacking = false;         // 동시 중복 공격 방지 락

    public float CurrentAngerMultiplier => currentAngerMultiplier;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 외부(스킬체크 실패, 비행 체류, 흡혈 중 위험)에서 호출하는 사람 공격 진입점
    /// </summary>
    public void TriggerAttack(Vector2 targetWorldPosition)
    {
        if (isAttacking) return; // 이미 공격 연출 진행 중이면 중첩 차단

        StartCoroutine(HandAttackRoutine(targetWorldPosition));
    }

    /// <summary>
    /// 손바닥 UI가 빨간색으로 차오르며 피격/회피를 판정하는 코루틴
    /// </summary>
    private IEnumerator HandAttackRoutine(Vector2 targetPosition)
    {
        isAttacking = true;

        // 1. 현재 분노에 따른 UI 차오름 시간 연산: $T_{\text{fill}} = \max(T_{\min}, \frac{T_{\text{base}}}{M_{\text{anger}}})$
        float fillDuration = Mathf.Max(minFillDuration, baseFillDuration / currentAngerMultiplier);

        Debug.LogWarning($"<color=red>[사람 공격 발동!] 분노 배율: {currentAngerMultiplier:F2}x | UI 차오름 시간: {fillDuration:F2}초</color>");

        // 2. 손바닥 UI 인스턴스 생성
        GameObject handInstance = Instantiate(handAttackUIPrefab, targetPosition, Quaternion.identity, worldCanvas.transform);
        Image fillImage = handInstance.GetComponentInChildren<Image>();

        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillAmount = 0f;
            fillImage.color = Color.red; // 빨간색으로 채워짐
        }

        // 3. 시간 흐름에 따른 Red Fill Amount 가공
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

        // 4. Fill 100% 완료! 찰싹 타격 및 범위 체크
        Collider2D hitMosquito = Physics2D.OverlapCircle(targetPosition, attackRadius, mosquitoLayer);

        if (hitMosquito != null && hitMosquito.TryGetComponent<MosquitoController>(out var mosquito))
        {
            // [결과 A] 모기가 범위 안에 아직 남아있음 -> 피격 성공!
            Debug.LogError("<color=red>[찰싹!] 모기를 명중시켰습니다!</color>");
            mosquito.OnHitByHumanHand();
        }
        else
        {
            // [결과 B] 모기가 차오르는 동안 범위 밖으로 도망침 -> 회피 성공!
            OnAttackDodged();
        }

        // 5. UI 정리 및 락 해제
        Destroy(handInstance);
        isAttacking = false;
    }

    /// <summary>
    /// 모기가 공격을 피했을 때 분노를 누적시키는 로직
    /// </summary>
    private void OnAttackDodged()
    {
        dodgeCount++;
        // 분노 배율 가증: $M_{\text{anger}} = 1.0 + (\text{DodgeCount} \times \Delta_{\text{anger}})$
        currentAngerMultiplier = 1.0f + (dodgeCount * angerPerDodge);

        Debug.Log($"<color=yellow>[회피 성공!] 사람이 더 화났습니다! (회피 횟수: {dodgeCount}회 | 분노 배율: {currentAngerMultiplier:F2}x)</color>");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}