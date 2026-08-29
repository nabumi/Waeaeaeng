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

    [Header("대시 및 회피(불릿 타임) 설정")]
    [Tooltip("대시 상태일 때 이동 속도 배율")]
    [SerializeField] private float dashSpeedMultiplier = 2.5f;
    [Tooltip("대시 유지 시간 (실시간 초 기준)")]
    [SerializeField] private float dashDuration = 0.25f;
    [Tooltip("대시 시 적용할 주변 시간 속도 (0.2 = 주변 시간이 5배 느려짐)")]
    [SerializeField] private float slowTimeScale = 0.2f;
    [Tooltip("대시 스킬 쿨타임 (실시간 초 기준)")]
    [SerializeField] private float dashCooldown = 1.0f;

    private bool isDashing = false;
    public bool IsDashing => isDashing;
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
    private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite deathSprite;
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
        spriteRenderer = GetComponentInChildren<SpriteRenderer>() ?? GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;

        if (deathSprite == null)
        {
            deathSprite = Resources.Load<Sprite>("Sprites/Mosquito_death");
        }

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

        AudioManager.Instance?.StartMosquitoBuzz();
    }

    private void FixedUpdate()
    {
        if (isDead || currentState == MosquitoState.Dead) return;

        if (isDashing)
        {
            float dashSpeed = effectiveMoveSpeed * dashSpeedMultiplier;
            rb.linearVelocity = currentDashDirection * (dashSpeed / Time.timeScale);
            return;
        }

        if (currentState == MosquitoState.Flying)
        {
            Vector2 targetVelocity = moveInput * effectiveMoveSpeed;

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
        if (isDead || currentState == MosquitoState.Dead) return;

        if (currentState == MosquitoState.Flying)
        {
            HandleKeyboardDashCheck();
            EvaluateFlyingLingerAttack();
        }
        else if (currentState == MosquitoState.Sucking)
        {
            ProcessBloodSucking();
            EvaluateSuckingDangerAttack();
        }
    }

    private void HandleKeyboardDashCheck()
    {
        if (isDashing) return;

        bool dashTriggered = false;
        if (Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame))
        {
            dashTriggered = true;
        }
        else if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            dashTriggered = true;
        }

        if (dashTriggered)
        {
            PerformDash();
        }
    }

    #region 대시(Dash) 및 불릿 타임 시스템

    private void OnDashInputReceived(InputAction.CallbackContext context)
    {
        if (!context.performed || isDead || currentState != MosquitoState.Flying || isDashing) return;
        PerformDash();
    }

    public void PerformDash()
    {
        if (Time.unscaledTime < lastDashTime + dashCooldown) return;

        lastDashTime = Time.unscaledTime;
        currentDashDirection = (moveInput != Vector2.zero) ? moveInput.normalized : lastNonZeroMoveDirection;

        StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        isDashing = true;
        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Dash);
        UpdateAnimationState();

        yield return new WaitForSecondsRealtime(dashDuration);

        ResetTimeScale();
        isDashing = false;
        UpdateAnimationState();
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
    }

    #endregion

    #region 4단계 흡혈 및 성장 연산

    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.BloodSuck);
        UpdateAnimationState();
    }

    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;
        UpdateAnimationState();
    }

    private void StartSuckingSequence()
    {
        currentState = MosquitoState.Sucking;
        currentZoneSuckedBlood = 0f; // 이번 세션 흡혈량 0으로 초기화

        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    private void ProcessBloodSucking()
    {
        if (suckAction != null && suckAction.IsPressed())
        {
            float previousBlood = currentBlood;
            float suckDelta = suckRate * Time.deltaTime;

            if (currentZoneMaxSuckAmount > 0f)
            {
                float remainInZone = currentZoneMaxSuckAmount - currentZoneSuckedBlood;
                suckDelta = Mathf.Min(suckDelta, remainInZone);
            }

            currentBlood = Mathf.Min(currentBlood + suckDelta, maxBlood);
            currentZoneSuckedBlood += (currentBlood - previousBlood);

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
            CheckTailGrowth();
            UpdateBodyGauge();

            if (currentZoneMaxSuckAmount > 0f && currentZoneSuckedBlood >= currentZoneMaxSuckAmount)
            {
                Debug.Log("<color=orange>[구역 고갈] 이 부위의 피를 모두 빨았습니다! 자국을 남기고 강제 이륙합니다.</color>");
                OnSuckingCompleted();
            }
            else if (currentBlood >= maxBlood)
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

    private void FinishBiteSession()
    {
        if (currentBitingZone != null && currentZoneSuckedBlood > 0f)
        {
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
            bodyGaugeRenderer.color = new Color(1f, 0f, 0f, fillRatio);
        }
    }

    private void CalculateEffectiveSpeed()
    {
        float debuffMultiplier = 1.0f - (currentTailLevel * speedDebuffPerLevel);
        effectiveMoveSpeed = baseMoveSpeed * Mathf.Max(debuffMultiplier, 0.2f);
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

        AudioManager.Instance?.StopMosquitoBuzz();
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Slap);
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.GameOver);

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

        if (HumanAngerManager.Instance != null)
        {
            HumanAngerManager.Instance.ResetAnger();
        }

        Debug.LogError("<color=red>========================================</color>");
        Debug.LogError("<color=red>[사망 연출] 모기가 피격되어 사망 모션을 재생합니다 (1.0초 후 게임오버 UI)</color>");
        Debug.LogError("<color=red>========================================</color>");

        StartCoroutine(DeathMotionRoutine());
    }

    private IEnumerator DeathMotionRoutine()
    {
        // 1. 애니메이터 비활성화하여 사망 스프라이트 유지
        if (animator != null) animator.enabled = false;

        // 2. 사망 스프라이트 교체
        if (spriteRenderer != null && deathSprite != null)
        {
            spriteRenderer.sprite = deathSprite;
        }

        // 3. 타격 피격 압축 연출 (납작하게 찌그러짐)
        Vector3 baseScale = transform.localScale;
        float timer = 0f;
        float flattenDuration = 0.15f;
        while (timer < flattenDuration)
        {
            timer += Time.deltaTime;
            float t = timer / flattenDuration;
            transform.localScale = new Vector3(baseScale.x * Mathf.Lerp(1.0f, 1.4f, t), baseScale.y * Mathf.Lerp(1.0f, 0.4f, t), baseScale.z);
            yield return null;
        }

        transform.localScale = new Vector3(baseScale.x * 1.4f, baseScale.y * 0.4f, baseScale.z);

        // 4. 납작해진 사망 상태로 1.0초 대기
        float remainingDelay = Mathf.Max(0f, 1.0f - flattenDuration);
        yield return new WaitForSeconds(remainingDelay);

        // 5. 정확히 1.0초 후 게임오버 결과창 출력
        Debug.LogError("<color=yellow>[GAME OVER] 1.0초 사망 모션 완료 -> 게임오버 결과창 페이드인</color>");
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

    public void OnLand(InputAction.CallbackContext context)
    {
        if (isDead || !context.performed || currentState != MosquitoState.Flying) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null) return;

        Vector3 landingPoint = hit.ClosestPoint(transform.position);

        BitingZone bZone = hit.GetComponent<BitingZone>() ?? hit.GetComponentInParent<BitingZone>();
        if (bZone != null)
        {
            if (bZone.IsPositionAlreadyBitten(landingPoint))
            {
                Debug.Log("[Mosquito] 이 위치 근처는 이미 물려서 자국이 남아있습니다!");
                return;
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
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteGreat);
            StartSuckingSequence();
        }
        else if (result == SkillCheckUI.SkillCheckResult.Success)
        {
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteSuccess);
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
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteFail);
            HumanAngerManager.Instance?.TriggerAttack(transform.position);

            FinishBiteSession();

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
            FinishBiteSession();

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