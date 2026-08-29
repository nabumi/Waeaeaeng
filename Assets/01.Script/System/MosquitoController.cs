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
    // [이벤트 시스템] 결과창 UI 및 외부 매니저 구독용
    // =========================================================================
    public static event Action OnGameOver;
    public event Action<float, float> OnBloodAmountChanged; // (현재 피, 최대 피)

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false;

    [Header("비행 및 이동 설정")]
    [SerializeField] private float baseMoveSpeed = 5f;
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
    private Vector2 currentDashDirection = Vector2.right;
    private Vector2 lastNonZeroMoveDirection = Vector2.right;

    [Header("피부 안착 감지 설정")]
    [SerializeField] private LayerMask humanSkinLayer;
    [SerializeField] private float landingRadius = 0.8f;

    [Header("공중 체류 공격 감지 설정")]
    [SerializeField] private float lingerCheckInterval = 1.0f;
    private float lingerTimer = 0f;

    [Header("스킬 체크 & 흡혈 기본 설정")]
    [SerializeField] private int requiredSkillChecks = 2;
    private int currentSkillCheckCount = 0;

    [SerializeField] private float maxBlood = 100f;
    [SerializeField] private float currentBlood = 0f;
    [SerializeField] private float suckRate = 25f; // 초당 흡혈 속도

    [Header("구역별 흡혈 세션 트래킹")]
    private BitingZone currentBitingZone; // 현재 안착해 있는 구역
    private float currentZoneMaxSuckAmount = 0f; // 해당 구역에서 빨 수 있는 최대 피
    private float currentZoneSuckedBlood = 0f;   // 이번 안착 세션에서 빤 피의 양

    [Header("꼬리 비대화 & 이동속도 디버프 설정")]
    [SerializeField] private float speedDebuffPerLevel = 0.15f; // 단계당 15% 감속
    [SerializeField] private Transform tailTransform;          // 꼬리 Transform
    [SerializeField] private SpriteRenderer bodyGaugeRenderer; // 피 게이지 SpriteRenderer

    // 꼬리 성장 기준 피 수치 (1단계: 5, 2단계: 15, 3단계: 30 이상 누적 시)
    [SerializeField] private int[] tailGrowthThresholds = new int[3] { 5, 15, 30 };
    [SerializeField]
    private Vector3[] tailScaleLevels = new Vector3[4]
    {
        new Vector3(1.0f, 1.0f, 1.0f), // 0단계 (기본)
        new Vector3(1.3f, 1.3f, 1.0f), // 1단계 (5 이상)
        new Vector3(1.6f, 1.6f, 1.0f), // 2단계 (15 이상)
        new Vector3(2.0f, 2.0f, 1.0f)  // 3단계 (30 이상, 최대)
    };

    private int currentTailLevel = 0;
    private float effectiveMoveSpeed; // 디버프가 적용된 실제 현재 속도

    [Header("흡혈 중 기습 공격 타이머")]
    [SerializeField] private float feedingCheckInterval = 1.0f;
    private float feedingTimer = 0f;
    private float currentZoneDangerRatio = 0f;

    // 컴포넌트 및 액션 캐싱
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;
    private InputAction takeOffAction;
    private InputAction dashAction;

    // 애니메이션 해시 캐싱 (GC 방지)
    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsLanding = Animator.StringToHash("IsLanding");
    private static readonly int HashIsChecking = Animator.StringToHash("IsChecking");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");
    private static readonly int HashIsDashing = Animator.StringToHash("IsDashing");

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
        CalculateEffectiveSpeed();
        UpdateTailVisual();
        UpdateBodyGauge();

        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
    }

    private void FixedUpdate()
    {
        if (isDead || currentState == MosquitoState.Dead) return;

        if (currentState == MosquitoState.Flying)
        {
            // 1. 대시 중일 때: 뚱뚱해진 실효 속도 연동 대시
            if (isDashing)
            {
                rb.linearVelocity = currentDashDirection * (effectiveMoveSpeed * dashSpeedMultiplier);
            }
            // 2. 일반 비행 상태일 때
            else
            {
                Vector2 targetVelocity = moveInput * effectiveMoveSpeed;

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
        if (Time.unscaledTime - lastDashTime < dashCooldown) return;

        if (moveInput != Vector2.zero)
            currentDashDirection = moveInput.normalized;
        else
            currentDashDirection = lastNonZeroMoveDirection;

        StartCoroutine(DashAndSlowMotionRoutine());
    }

    private IEnumerator DashAndSlowMotionRoutine()
    {
        isDashing = true;
        lastDashTime = Time.unscaledTime;

        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        UpdateAnimationState();

        yield return new WaitForSecondsRealtime(dashDuration);

        isDashing = false;
        ResetTimeScale();

        UpdateAnimationState();
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
    }

    #endregion

    #region 4단계 흡혈, 스킬체크 및 꼬리 성장 연산

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
        currentZoneSuckedBlood = 0f; // 흡혈 세션 초기화

        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();
    }

    /// <summary>
    /// Sucking 상태에서 실시간으로 피를 빨아들이는 핵심 프레임 로직
    /// </summary>
    private void ProcessBloodSucking()
    {
        // 입력 키(좌클릭)를 누르고 있을 때만 실제로 피를 뺘아들임
        if (suckAction != null && !suckAction.IsPressed()) return;

        if (currentBlood < maxBlood && currentZoneSuckedBlood < currentZoneMaxSuckAmount)
        {
            // 프레임당 흡혈량 계산
            float deltaSuck = suckRate * Time.deltaTime;

            // 구역 남은 한도 및 전체 최대 피($100$) 한도 보정
            float remainingZoneCap = currentZoneMaxSuckAmount - currentZoneSuckedBlood;
            float remainingTotalCap = maxBlood - currentBlood;
            float actualSuck = Mathf.Min(deltaSuck, Mathf.Min(remainingZoneCap, remainingTotalCap));

            if (actualSuck > 0f)
            {
                currentBlood += actualSuck;
                currentZoneSuckedBlood += actualSuck;

                // UI 및 비주얼 동기화
                OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
                UpdateBodyGauge();
                CheckTailGrowth();
            }

            // 구역 한도 도달 또는 총 피 100 도달 시 자동 이탈 처리
            if (currentZoneSuckedBlood >= currentZoneMaxSuckAmount || Mathf.Approximately(currentBlood, maxBlood))
            {
                OnSuckingCompleted();
            }
        }
    }

    private void OnSuckingCompleted()
    {
        FinishBiteSession();

        currentState = MosquitoState.Flying;
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    /// <summary>
    /// 흡혈 세션 종료 시 해당 구역에 흉터(자국)를 남기고 차단하는 로직
    /// </summary>
    private void FinishBiteSession()
    {
        if (currentBitingZone != null && currentZoneSuckedBlood > 0f)
        {
            // 현재 위치(transform.position)에 자국 등록 및 생성
            currentBitingZone.RegisterBiteMark(transform.position);
            currentBitingZone = null;
        }
    }

    private void CheckTailGrowth()
    {
        int newLevel = 0;

        for (int i = 0; i < tailGrowthThresholds.Length; i++)
        {
            if (currentBlood >= tailGrowthThresholds[i])
            {
                newLevel = i + 1;
            }
        }

        if (newLevel != currentTailLevel)
        {
            currentTailLevel = Mathf.Min(newLevel, 3);
            UpdateTailVisual();
            CalculateEffectiveSpeed();
        }
    }

    private void UpdateTailVisual()
    {
        if (tailTransform != null && currentTailLevel < tailScaleLevels.Length)
        {
            tailTransform.localScale = tailScaleLevels[currentTailLevel];
        }
    }

    private void UpdateBodyGauge()
    {
        if (bodyGaugeRenderer != null)
        {
            float fillRatio = Mathf.Clamp01(currentBlood / maxBlood);
            // 피가 차오를수록 알파값 또는 색상을 빨간색으로 채움
            bodyGaugeRenderer.color = new Color(1f, 0f, 0f, fillRatio);
        }
    }

    private void CalculateEffectiveSpeed()
    {
        float debuffMultiplier = 1.0f - (currentTailLevel * speedDebuffPerLevel);
        effectiveMoveSpeed = baseMoveSpeed * Mathf.Max(debuffMultiplier, 0.2f); // 최소 20% 속도 보장

        Debug.Log($"[Mosquito Engine] 꼬리 단계: {currentTailLevel} | 현재 이동 속도: {effectiveMoveSpeed}");
    }

    #endregion

    #region 기습 공격 및 게임오버/리스폰 처리

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
        currentTailLevel = 0;

        CalculateEffectiveSpeed();
        UpdateTailVisual();
        UpdateBodyGauge();

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

        if (moveInput != Vector2.zero)
        {
            lastNonZeroMoveDirection = moveInput.normalized;
        }
    }

    /// <summary>
    /// 피부 착지 입력 처리 (오타 수정 반영)
    /// </summary>
    public void OnLand(InputAction.CallbackContext context)
    {
        if (isDead || !context.performed || currentState != MosquitoState.Flying) return;

        // 1. 피부 Layer 충돌체 검출 (변수명: hit)
        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null) return;

        // [오타 수정] skin.ClosestPoint -> hit.ClosestPoint 로 변경!
        Vector3 landingPoint = hit.ClosestPoint(transform.position);

        // 2. BitingZone 위치 기반 중복 검사
        BitingZone bZone = hit.GetComponent<BitingZone>() ?? hit.GetComponentInParent<BitingZone>();
        if (bZone != null)
        {
            if (bZone.IsPositionAlreadyBitten(landingPoint))
            {
                Debug.Log("[Mosquito] 이 위치 근처는 이미 물려서 자국이 남아있습니다!");
                return; // 착지 거부
            }

            currentBitingZone = bZone;
            currentZoneMaxSuckAmount = bZone.GetMaxSuckAmount();
        }

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

            FinishBiteSession(); // QTE 실패 시 이탈 처리

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
            FinishBiteSession(); // 중간 도망 시 흡혈 세션 종료 및 자국 남기기

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;

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

        if (isDashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, currentDashDirection * 2f);
        }
    }
}