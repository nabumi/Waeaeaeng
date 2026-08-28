using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MosquitoState
{
    Flying,          // 공중 비행
    SkillChecking,   // QTE 스킬체크 진행 중
    Feeding,         // 피부 안착 및 흡혈 가능 상태
    Stunned          // 손바닥 피격 스턴
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

    [Header("공중 체류 공격 감지 설정")]
    [SerializeField] private float lingerCheckInterval = 1.0f;
    private float lingerTimer = 0f;

    [Header("스킬 체크 & 흡혈 설정")]
    [SerializeField] private int requiredSkillChecks = 2;
    private int currentSkillCheckCount = 0;

    [SerializeField] private float maxBlood = 100f;
    [SerializeField] private float currentBlood = 0f;
    [SerializeField] private float suckRate = 25f; // 초당 흡혈량 ($R_{\text{suck}}$)

    // [핵심] 좌클릭 누름 유무를 판별하는 플래그
    [SerializeField] private bool isSucking = false;

    [Header("흡혈 중 기습 공격 타이머")]
    [SerializeField] private float feedingCheckInterval = 1.0f;
    private float feedingTimer = 0f;
    private float currentZoneDangerRatio = 0f;

    // UI 동기화 이벤트 (현재 혈액량, 최대 혈액량)
    public event Action<float, float> OnBloodAmountChanged;

    // 컴포넌트 캐싱
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;      // 좌클릭 흡혈 액션
    private InputAction takeOffAction;   // 이륙 액션

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

        // 좌클릭 눌렀을 때와 뗐을 때 이벤트 바인딩
        if (suckAction != null)
        {
            suckAction.started += OnSuckStarted;   // 좌클릭 눌림 시작
            suckAction.canceled += OnSuckCanceled; // 좌클릭 뗌
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
        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
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
        if (currentState == MosquitoState.Flying)
        {
            EvaluateFlyingLingerAttack();
        }
        else if (currentState == MosquitoState.Feeding)
        {
            // [수동 흡혈] 좌클릭을 누르고 있는 동안(isSucking == true)에만 흡혈 처리
            if (isSucking)
            {
                ProcessBloodSucking();
            }

            // 피를 빠는 도중에만 위험도 판정을 강화해 기습 공격 체크
            EvaluateFeedingDangerAttack();
        }
    }

    #region 수동 흡혈 입력 및 코어 연산

    /// <summary>
    /// 좌클릭을 누르기 시작했을 때 (InputAction.started)
    /// </summary>
    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (currentState != MosquitoState.Feeding) return;

        isSucking = true;
        UpdateAnimationState();
        Debug.Log("<color=red>[흡혈 중...] 좌클릭 홀드: 피를 빨기 시작합니다!</color>");
    }

    /// <summary>
    /// 좌클릭에서 손을 뗐을 때 (InputAction.canceled)
    /// </summary>
    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (currentState != MosquitoState.Feeding) return;

        isSucking = false;
        UpdateAnimationState();
        Debug.Log("<color=yellow>[흡혈 중단] 좌클릭 해제: 흡혈을 일시 멈춥니다.</color>");
    }

    /// <summary>
    /// 스킬체크 성공 후 흡혈 가능 단계로 진입하는 함수
    /// </summary>
    private void StartFeedingSequence()
    {
        currentState = MosquitoState.Feeding;
        feedingTimer = 0f;

        // [수정] 자동 흡혈 방지! 좌클릭을 누르기 전까지는 대기 상태
        isSucking = false;

        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();

        Debug.Log("<color=green>[안착 완료] 좌클릭을 꾹 눌러 피를 빠세요!</color>");
    }

    /// <summary>
    /// 프레임 단위 실시간 흡혈 연산
    /// </summary>
    private void ProcessBloodSucking()
    {
        if (currentBlood < maxBlood)
        {
            // $B(t + \Delta t) = \min(B_{\max}, B(t) + R_{\text{suck}} \cdot \Delta t)$
            currentBlood += suckRate * Time.deltaTime;
            currentBlood = Mathf.Min(currentBlood, maxBlood);

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);

            // 피가 $100\%$ 다 차면 자동으로 완료 처리 후 이륙
            if (Mathf.Approximately(currentBlood, maxBlood))
            {
                OnFeedingCompleted();
            }
        }
    }

    private void OnFeedingCompleted()
    {
        Debug.Log("<color=cyan>[흡혈 완수!] 피를 최대로 채워 자동으로 이륙합니다.</color>");
        isSucking = false;
        currentState = MosquitoState.Flying;

        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    #endregion

    #region 기습 공격 및 스턴 연산

    private void EvaluateFlyingLingerAttack()
    {
        lingerTimer += Time.deltaTime;
        if (lingerTimer < lingerCheckInterval) return;
        lingerTimer = 0f;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit != null)
        {
            IBodyPartZone zone = hit.GetComponent<IBodyPartZone>() ?? hit.GetComponentInParent<IBodyPartZone>();
            if (zone != null)
            {
                float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;
                float attackProbability = zone.DangerProbability * angerMult * 0.25f;

                if (UnityEngine.Random.value <= attackProbability)
                {
                    HumanAngerManager.Instance?.TriggerAttack(transform.position);
                }
            }
        }
    }

    private void EvaluateFeedingDangerAttack()
    {
        feedingTimer += Time.deltaTime;
        if (feedingTimer < feedingCheckInterval) return;
        feedingTimer = 0f;

        float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;

        // 피를 빠는 중(isSucking)이면 위험 배율 2배 증가!
        float suckRiskMultiplier = isSucking ? 2.0f : 0.5f;
        float attackProb = currentZoneDangerRatio * angerMult * 0.3f * suckRiskMultiplier;

        if (UnityEngine.Random.value <= attackProb)
        {
            Debug.LogWarning("<color=red>[위협 감지] 흡혈 통증으로 사람이 손을 내리칩니다!</color>");
            HumanAngerManager.Instance?.TriggerAttack(transform.position);
        }
    }

    public void OnHitByHumanHand()
    {
        StopAllCoroutines();
        StartCoroutine(StunAndKnockbackRoutine());
    }

    private IEnumerator StunAndKnockbackRoutine()
    {
        currentState = MosquitoState.Stunned;
        isSucking = false; // 피격 시 흡혈 상태 해제

        Vector2 knockbackDir = (UnityEngine.Random.insideUnitCircle + Vector2.up).normalized;
        rb.linearVelocity = knockbackDir * 7f;

        yield return new WaitForSeconds(1.0f);

        currentState = MosquitoState.Flying;
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    #endregion

    #region Input & QTE Handlers

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

            if (currentSkillCheckCount >= requiredSkillChecks)
            {
                StartFeedingSequence(); // 스킬체크 2회 성공 시 안착 및 흡혈 대기 모드 진입
            }
            else
            {
                SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
            }
        }
        else // Fail
        {
            HumanAngerManager.Instance?.TriggerAttack(transform.position);

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void OnCheckInputReceived(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.SkillChecking && SkillCheckUI.Instance != null)
            SkillCheckUI.Instance.OnInputPressed();
    }

    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        // 흡혈 상태일 때 수동 이륙 기능
        if (currentState == MosquitoState.Feeding)
        {
            isSucking = false;
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
        animator.SetBool(HashIsSucking, isSucking);
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