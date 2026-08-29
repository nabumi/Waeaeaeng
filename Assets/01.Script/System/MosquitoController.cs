using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모기의 현재 행동 상태 정의 (4단계 핵심 루프 + 사망)
/// </summary>
public enum MosquitoState
{
    Flying,     // 1단계: 공중 비행 및 이동
    Landing,    // 2단계: 피부에 척 달라붙는 안착 모션
    Checking,   // 3단계: 빨대를 꽂고 QTE 스킬체크 진행
    Sucking,    // 4단계: 좌클릭을 꾹 눌러 피를 빠는 상태
    Dead        // 5단계: 사망 (게임 오버)
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    // =========================================================================
    // [게임오버 이벤트] 결과창 UI 스크립트 등에서 구독
    // =========================================================================
    public static event Action OnGameOver;

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false;

    [Header("비행 및 부유 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float hoverAmplitude = 0.15f;
    [SerializeField] private float hoverFrequency = 4f;

    [Header("대시 및 초격차 감각(불릿 타임) 설정")]
    [Tooltip("대시 상태일 때 이동 속도 배율")]
    [SerializeField] private float dashSpeedMultiplier = 2.5f;

    [Tooltip("초격차 감각 유지 시간 (실시간 초 기준)")]
    [SerializeField] private float dashDuration = 0.35f;

    [Tooltip("대시 시 적용할 주변 시간 속도 (0.2 = 주변 시간이 5배 느려짐)")]
    [SerializeField] private float slowTimeScale = 0.2f;

    [Tooltip("대시 스킬 쿨타임 (실시간 초 기준)")]
    [SerializeField] private float dashCooldown = 1.5f;

    private bool isDashing = false;
    private float lastDashTime = -999f;

    // 대시 순간 고정되는 방향 벡터 및 방향 기억 변수
    private Vector2 currentDashDirection = Vector2.right;
    private Vector2 lastNonZeroMoveDirection = Vector2.right; // 기본 우측 바라봄

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
    [SerializeField] private float suckRate = 25f;

    [Header("흡혈 중 기습 공격 타이머")]
    [SerializeField] private float feedingCheckInterval = 1.0f;
    private float feedingTimer = 0f;
    private float currentZoneDangerRatio = 0f;

    // UI 동기화 이벤트
    public event Action<float, float> OnBloodAmountChanged;

    // 컴포넌트 및 액션 캐싱
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;
    private InputAction takeOffAction;
    private InputAction dashAction;

    // 애니메이션 해시 최적화 캐싱
    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsLanding = Animator.StringToHash("IsLanding");
    private static readonly int HashIsChecking = Animator.StringToHash("IsChecking");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");
    private static readonly int HashIsDashing = Animator.StringToHash("IsDashing"); // Bool 제어용 파라미터

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
            dashAction = playerInput.actions.FindAction("Dash");
        }
    }

    private void OnEnable()
    {
        if (checkAction != null) checkAction.performed += OnCheckInputReceived;
        if (takeOffAction != null) takeOffAction.performed += OnTakeOffInputReceived;
        if (dashAction != null) dashAction.performed += OnDashInputReceived;

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
        if (dashAction != null) dashAction.performed -= OnDashInputReceived;

        if (suckAction != null)
        {
            suckAction.started -= OnSuckStarted;
            suckAction.canceled -= OnSuckCanceled;
        }

        ResetTimeScale();
    }

    private void Start()
    {
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
    }

    private void FixedUpdate()
    {
        if (isDead || currentState == MosquitoState.Dead) return;

        if (currentState == MosquitoState.Flying)
        {
            // 1. 대시 중일 때: 고정된 대시 방향으로 호버링 없이 직선 강타
            if (isDashing)
            {
                rb.linearVelocity = currentDashDirection * (moveSpeed * dashSpeedMultiplier);
            }
            // 2. 일반 비행 상태일 때
            else
            {
                Vector2 targetVelocity = moveInput * moveSpeed;

                if (moveInput == Vector2.zero)
                {
                    float hoverVy = hoverAmplitude * hoverFrequency * Mathf.Cos(Time.fixedTime * hoverFrequency);
                    targetVelocity.y = hoverVy;
                }

                rb.linearVelocity = targetVelocity;
            }
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void Update()
    {
        if (isDead || currentState == MosquitoState.Dead) return;

        if (currentState == MosquitoState.Flying)
        {
            EvaluateFlyingLingerAttack();
        }
        else if (currentState == MosquitoState.Sucking)
        {
            ProcessBloodSucking();
            EvaluateSuckingDangerAttack();
        }
    }

    #region 대시 & 초격차 감각 로직

    private void OnDashInputReceived(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Flying || isDashing) return;

        // 쿨타임 검사 (실시간 기준)
        if (Time.unscaledTime - lastDashTime < dashCooldown) return;

        // 대시 방향 결정: 입력 중이면 입력 방향, 정지 중이면 마지막으로 입력했던 방향
        if (moveInput != Vector2.zero)
        {
            currentDashDirection = moveInput.normalized;
        }
        else
        {
            currentDashDirection = lastNonZeroMoveDirection;
        }

        StartCoroutine(DashAndSlowMotionRoutine());
    }

    private IEnumerator DashAndSlowMotionRoutine()
    {
        isDashing = true;
        lastDashTime = Time.unscaledTime;

        // 불릿 타임(슬로우 모션) 적용
        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        // 대시 애니메이션 실행 (IsDashing = true)
        UpdateAnimationState();

        // 실시간 기준 대시 시간만큼 대기
        yield return new WaitForSecondsRealtime(dashDuration);

        // 대시 상태 해제 및 시간 정상화
        isDashing = false;
        ResetTimeScale();

        // 애니메이션을 Flying으로 원복 (IsDashing = false)
        UpdateAnimationState();
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
    }

    #endregion

    #region 4단계 흡혈 및 QTE 연산

    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;
        UpdateAnimationState();
    }

    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;
        UpdateAnimationState();
    }

    private void StartSuckingSequence()
    {
        if (isDead) return;

        currentState = MosquitoState.Sucking;
        feedingTimer = 0f;

        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();
    }

    private void ProcessBloodSucking()
    {
        if (currentBlood < maxBlood)
        {
            currentBlood += suckRate * Time.deltaTime;
            currentBlood = Mathf.Min(currentBlood, maxBlood);

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);

            if (Mathf.Approximately(currentBlood, maxBlood))
            {
                OnSuckingCompleted();
            }
        }
    }

    private void OnSuckingCompleted()
    {
        currentState = MosquitoState.Flying;
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    #endregion

    #region 기습 공격 및 게임오버 처리

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

    private void EvaluateSuckingDangerAttack()
    {
        feedingTimer += Time.deltaTime;
        if (feedingTimer < feedingCheckInterval) return;
        feedingTimer = 0f;

        float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;

        float suckRiskMultiplier = (suckAction != null && suckAction.IsPressed()) ? 2.0f : 0.5f;
        float attackProb = currentZoneDangerRatio * angerMult * 0.3f * suckRiskMultiplier;

        if (UnityEngine.Random.value <= attackProb)
        {
            HumanAngerManager.Instance?.TriggerAttack(transform.position);
        }
    }

    public void OnHitByHumanHand()
    {
        if (isDead) return;

        isDead = true;
        currentState = MosquitoState.Dead;

        StopAllCoroutines();
        ResetTimeScale();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        if (playerInput != null)
        {
            playerInput.enabled = false;
        }

        UpdateAnimationState();
        OnGameOver?.Invoke();
    }

    public void RespawnMosquito(Vector3 spawnPosition)
    {
        isDead = false;
        currentState = MosquitoState.Flying;
        transform.position = spawnPosition;
        currentBlood = 0f;

        ResetTimeScale();

        if (rb != null)
        {
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
        }

        if (playerInput != null)
        {
            playerInput.enabled = true;
        }

        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
    }

    #endregion

    #region Input & QTE Handlers

    public void OnMove(InputAction.CallbackContext context)
    {
        if (isDead) return;
        moveInput = context.ReadValue<Vector2>();

        // 이동 중일 때 마지막 이동 방향 최신화
        if (moveInput != Vector2.zero)
        {
            lastNonZeroMoveDirection = moveInput.normalized;
        }
    }

    public void OnLand(InputAction.CallbackContext context)
    {
        if (isDead || !context.performed || currentState != MosquitoState.Flying) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null) return;

        IBodyPartZone zone = hit.GetComponent<IBodyPartZone>() ?? hit.GetComponentInParent<IBodyPartZone>();
        if (zone != null)
        {
            currentZoneDangerRatio = zone.DangerProbability;
            StartLandingSequence(hit, currentZoneDangerRatio);
        }
    }

    private void StartLandingSequence(Collider2D skin, float dangerRatio)
    {
        if (isDead) return;

        currentState = MosquitoState.Landing;
        transform.position = skin.ClosestPoint(transform.position);

        ResetTimeScale();
        SwitchActionMapSafely("SkillCheck");
        UpdateAnimationState();

        StartCoroutine(WaitLandingAnimationRoutine(dangerRatio));
    }

    private IEnumerator WaitLandingAnimationRoutine(float dangerRatio)
    {
        yield return new WaitForSeconds(0.3f);

        if (isDead) yield break;

        currentState = MosquitoState.Checking;
        currentSkillCheckCount = 0;
        UpdateAnimationState();

        if (SkillCheckUI.Instance != null)
        {
            SkillCheckUI.Instance.BeginSkillCheck(1f + dangerRatio, OnDbdSkillCheckCompleted);
        }
    }

    private void OnDbdSkillCheckCompleted(SkillCheckUI.SkillCheckResult result)
    {
        if (isDead) return;

        if (result == SkillCheckUI.SkillCheckResult.GreatSuccess)
        {
            StartSuckingSequence();
        }
        else if (result == SkillCheckUI.SkillCheckResult.Success)
        {
            currentSkillCheckCount++;

            if (currentSkillCheckCount >= requiredSkillChecks)
            {
                StartSuckingSequence();
            }
            else
            {
                SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
            }
        }
        else
        {
            HumanAngerManager.Instance?.TriggerAttack(transform.position);

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void OnCheckInputReceived(InputAction.CallbackContext context)
    {
        if (isDead) return;

        if (currentState == MosquitoState.Checking && SkillCheckUI.Instance != null)
            SkillCheckUI.Instance.OnInputPressed();
    }

    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        if (isDead) return;

        if (currentState == MosquitoState.Sucking || currentState == MosquitoState.Checking)
        {
            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    /// <summary>
    /// 모기의 현재 상태 및 대시 상태를 애니메이터와 완벽히 동기화
    /// </summary>
    private void UpdateAnimationState()
    {
        if (animator == null) return;

        // 대시 중일 때는 대시 애니메이션 전용 파라미터 반영
        animator.SetBool(HashIsDashing, isDashing);

        animator.SetBool(HashIsFlying, currentState == MosquitoState.Flying && !isDashing);
        animator.SetBool(HashIsLanding, currentState == MosquitoState.Landing);
        animator.SetBool(HashIsChecking, currentState == MosquitoState.Checking);

        bool isHoldingSuck = (currentState == MosquitoState.Sucking && suckAction != null && suckAction.IsPressed());
        animator.SetBool(HashIsSucking, isHoldingSuck);
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

        // 대시 방향 시각화 디버깅
        if (isDashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, currentDashDirection * 2f);
        }
    }
}