using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MosquitoState
{
    Flying,          // 자유 비행
    SkillChecking,   // QTE 진행 중
    Feeding,         // 흡혈 진행 중
    Stunned          // 손바닥에 피격되어 스턴
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;

    [Header("비행 및 부유 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float hoverAmplitude = 0.15f;
    [SerializeField] private float hoverFrequency = 4f;

    [Header("피부 안착 감지 설정")]
    [SerializeField] private LayerMask humanSkinLayer;
    [SerializeField] private float landingRadius = 0.8f;

    [Header("공중 체류(Linger) 공격 감지 설정")]
    [SerializeField] private float lingerCheckInterval = 1.0f; // 비행 중 몇 초마다 체류 위험을 연산할 것인가
    private float lingerTimer = 0f;

    [Header("흡혈 및 스킬체크 설정")]
    [SerializeField] private int requiredSkillChecks = 2;
    private int currentSkillCheckCount = 0;
    [SerializeField] private float maxBlood = 100f;
    private float currentBlood = 0f;
    [SerializeField] private float suckRate = 20f;
    private bool isSucking = false;

    [Header("흡혈 중 기습 공격 타이머")]
    [SerializeField] private float feedingCheckInterval = 1.5f;
    private float feedingTimer = 0f;
    private float currentZoneDangerRatio = 0f; // 현재 안착한 부위의 위험도 (0.0~1.0)

    // 캐싱 컴포넌트
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;
    private InputAction takeOffAction;

    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsFeeding = Animator.StringToHash("IsFeeding");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerInput = GetComponent<PlayerInput>();
        animator = GetComponentInChildren<Animator>();
        rb.gravityScale = 0f;

        if (playerInput != null && playerInput.actions != null)
        {
            checkAction = playerInput.actions.FindAction("Check");
            suckAction = playerInput.actions.FindAction("Suck");
            takeOffAction = playerInput.actions.FindAction("TakeOff");
        }
    }

    private void OnEnable()
    {
        if (checkAction != null) checkAction.performed += OnCheckInputReceived;
        if (takeOffAction != null) takeOffAction.performed += OnTakeOffInputReceived;
        if (suckAction != null)
        {
            suckAction.started += OnSuckStarted;
            suckAction.canceled += OnSuckCanceled;
        }
    }

    private void OnDisable()
    {
        if (checkAction != null) checkAction.performed -= OnCheckInputReceived;
        if (takeOffAction != null) takeOffAction.performed -= OnTakeOffInputReceived;
        if (suckAction != null)
        {
            suckAction.started -= OnSuckStarted;
            suckAction.canceled -= OnSuckCanceled;
        }
    }

    private void Start()
    {
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    private void FixedUpdate()
    {
        if (currentState == MosquitoState.Flying)
        {
            Vector2 targetVelocity = moveInput * moveSpeed;
            if (moveInput == Vector2.zero)
            {
                float hoverVy = hoverAmplitude * hoverFrequency * Mathf.Cos(Time.fixedTime * hoverFrequency);
                targetVelocity.y = hoverVy;
            }
            rb.linearVelocity = targetVelocity;
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void Update()
    {
        // 1. 공중 비행(`Flying`) 중 영역 체류 시 사람 공격 확률 체크 (패턴 2)
        if (currentState == MosquitoState.Flying)
        {
            EvaluateFlyingLingerAttack();
        }
        // 2. 흡혈(`Feeding`) 중 위험도 누적 공격 체크 (패턴 3)
        else if (currentState == MosquitoState.Feeding)
        {
            if (isSucking) ProcessBloodSucking();
            EvaluateFeedingDangerAttack();
        }
    }

    #region Attack Pattern Evaluators (핵심 패턴 연산)

    /// <summary>
    /// [패턴 2] 공중에서 비행만 하며 정체되어 있을 때 영역 확률 기반 기습 공격
    /// </summary>
    private void EvaluateFlyingLingerAttack()
    {
        lingerTimer += Time.deltaTime;
        if (lingerTimer < lingerCheckInterval) return;
        lingerTimer = 0f;

        // 현재 모기 주변에 있는 피부 감지
        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit != null)
        {
            IBodyPartZone zone = hit.GetComponent<IBodyPartZone>() ?? hit.GetComponentInParent<IBodyPartZone>();
            if (zone != null)
            {
                // 확률 연산: $P = P_{\text{zone}} \times M_{\text{anger}} \times 0.25$
                float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;
                float attackProbability = zone.DangerProbability * angerMult * 0.25f;

                if (Random.value <= attackProbability)
                {
                    Debug.LogWarning($"<color=red>[기습] 비행 중 오래 얼씬거려 사람이 손을 휘두릅니다! (확률: {attackProbability * 100f:F1}%)</color>");
                    HumanAngerManager.Instance?.TriggerAttack(transform.position);
                }
            }
        }
    }

    /// <summary>
    /// [패턴 3] 흡혈 중 누적 위험도 기반 공격 확률 체크
    /// </summary>
    private void EvaluateFeedingDangerAttack()
    {
        feedingTimer += Time.deltaTime;
        if (feedingTimer < feedingCheckInterval) return;
        feedingTimer = 0f;

        float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;
        float attackProb = currentZoneDangerRatio * angerMult * 0.4f;

        if (Random.value <= attackProb)
        {
            Debug.LogWarning($"<color=red>[기습] 피를 빨리는 통증을 느끼고 사람이 손을 내리칩니다!</color>");
            HumanAngerManager.Instance?.TriggerAttack(transform.position);
        }
    }

    /// <summary>
    /// 손바닥 타격 판정에 최종 피격당했을 때 호출
    /// </summary>
    public void OnHitByHumanHand()
    {
        StopAllCoroutines();
        StartCoroutine(StunAndKnockbackRoutine());
    }

    private IEnumerator StunAndKnockbackRoutine()
    {
        currentState = MosquitoState.Stunned;
        Debug.LogError("<color=red>[피격!] 손바닥에 맞아 스턴 상태가 되었습니다!</color>");

        // 강제 넉백 물리 부여
        Vector2 knockbackDir = (Random.insideUnitCircle + Vector2.up).normalized;
        rb.linearVelocity = knockbackDir * 7f;

        yield return new WaitForSeconds(1.0f); // 1초간 정신 못 차림

        currentState = MosquitoState.Flying;
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    #endregion

    #region Input Handlers & State Transitions

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLand(InputAction.CallbackContext context)
    {
        if (!context.performed || currentState != MosquitoState.Flying) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null) return;

        IBodyPartZone zone = hit.GetComponent<IBodyPartZone>() ?? hit.GetComponentInParent<IBodyPartZone>();
        if (zone != null)
        {
            currentZoneDangerRatio = zone.DangerProbability;
            StartSkillCheckSequence(hit, currentZoneDangerRatio);
        }
    }

    private void StartSkillCheckSequence(Collider2D skin, float dangerRatio)
    {
        currentState = MosquitoState.SkillChecking;
        currentSkillCheckCount = 0;

        transform.position = skin.ClosestPoint(transform.position);
        SwitchActionMapSafely("SkillCheck");
        UpdateAnimationState();

        if (SkillCheckUI.Instance != null)
        {
            SkillCheckUI.Instance.BeginSkillCheck(1f + dangerRatio, OnDbdSkillCheckCompleted);
        }
    }

    private void OnDbdSkillCheckCompleted(SkillCheckUI.SkillCheckResult result)
    {
        if (result == SkillCheckUI.SkillCheckResult.GreatSuccess)
        {
            StartFeedingSequence();
        }
        else if (result == SkillCheckUI.SkillCheckResult.Success)
        {
            currentSkillCheckCount++;
            if (currentSkillCheckCount >= requiredSkillChecks) StartFeedingSequence();
            else SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
        }
        else // [패턴 1] 스킬체크 실패 -> 즉시 공격 발동!
        {
            Debug.LogWarning("<color=red>[스킬체크 실패!] 사람 공격 트리거 즉시 발동!</color>");
            HumanAngerManager.Instance?.TriggerAttack(transform.position);

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void StartFeedingSequence()
    {
        currentState = MosquitoState.Feeding;
        feedingTimer = 0f;
        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();
    }

    private void ProcessBloodSucking()
    {
        if (currentBlood < maxBlood)
        {
            currentBlood = Mathf.Min(currentBlood + suckRate * Time.deltaTime, maxBlood);
        }
    }

    private void OnCheckInputReceived(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.SkillChecking && SkillCheckUI.Instance != null)
            SkillCheckUI.Instance.OnInputPressed();
    }

    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.Feeding)
        {
            isSucking = true;
            if (animator != null) animator.SetBool(HashIsSucking, true);
        }
    }

    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.Feeding)
        {
            isSucking = false;
            if (animator != null) animator.SetBool(HashIsSucking, false);
        }
    }

    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.Feeding && !isSucking)
        {
            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;
        animator.SetBool(HashIsFlying, currentState == MosquitoState.Flying);
        animator.SetBool(HashIsFeeding, currentState == MosquitoState.Feeding);
        if (currentState != MosquitoState.Feeding)
        {
            isSucking = false;
            animator.SetBool(HashIsSucking, false);
        }
    }

    private void SwitchActionMapSafely(string mapName)
    {
        if (playerInput != null && playerInput.actions.FindActionMap(mapName) != null)
            playerInput.SwitchCurrentActionMap(mapName);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, landingRadius);
    }
}