using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모기의 행동 상태 정의 (비행, 안착, 스킬체크, 흡혈, 사망)
/// </summary>
public enum MosquitoState
{
    Flying,     // 1단계: 공중 비행 및 이동
    Landing,    // 2단계: 피부 안착 모션
    Checking,   // 3단계: QTE 스킬체크 진행
    Sucking,    // 4단계: 흡혈 진행
    Dead        // 5단계: 사망
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    // =========================================================================
    // [이벤트 시스템]
    // =========================================================================
    public static event Action OnMosquitoDied;
    public event Action<float, float> OnBloodAmountChanged; // (현재 피, 최대 피)

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false;

    [Header("시각 연출 및 꼬리 동기화")]
    [Tooltip("자식 Visual 오브젝트에 붙은 꼬리 동기화 컴포넌트")]
    [SerializeField] private MosquitoTailSync tailSync;
    [SerializeField] private bool isFacingRight = true; // 모기가 기본적으로 오른쪽을 바라보고 있는지 여부

    [Header("비행 및 이동 설정")]
    [SerializeField] private float baseMoveSpeed = 5;
    [SerializeField] private float hoverAmplitude = 0.15f;
    [SerializeField] private float hoverFrequency = 4f;

    [Header("혈액 생존 및 소모 설정 (Energy Drain)")]
    [Tooltip("비행 중 초당 혈액 자연 소모량 (ml/s)")]
    [SerializeField] private float flightBloodDrainRate = 1.5f;

    [Tooltip("대시 사용 시 1회 혈액 소모량 (ml)")]
    [SerializeField] private float dashBloodCost = 5.0f;

    [Header("대시 및 불릿 타임 설정")]
    [SerializeField] private float dashSpeedMultiplier = 1.5f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float slowTimeScale = 0.2f;
    [SerializeField] private float dashCooldown = 0.6f;

    private bool isDashing = false;
    public bool IsDashing => isDashing;
    private float lastDashTime = -999f;
    private Vector2 currentDashDirection = Vector2.right;

    [Header("피부 안착 및 위험도 설정")]
    [SerializeField] private LayerMask humanSkinLayer;
    [SerializeField] private float landingRadius = 0.8f;
    [SerializeField] private float checkInterval = 1.0f;
    private float attackCheckTimer = 0f;
    private float currentZoneDangerRatio = 0f;

    [Header("스킬 체크 & 흡혈 설정")]
    [SerializeField] private int requiredSkillChecks = 2;
    private int currentSkillCheckCount = 0;

    [SerializeField] private float maxBlood = 200f;
    [SerializeField] private float escapeThreshold = 150f;
    [SerializeField] private float currentBlood = 40f;
    [SerializeField] private float suckRate = 25f; // 초당 흡혈 속도

    [Header("구역별 세션 트래킹")]
    private BitingZone currentBitingZone;
    private float currentZoneMaxSuckAmount = 0f;
    private float currentZoneSuckedBlood = 0f;

    [Header("이동속도 디버프 설정")]
    [SerializeField] private float speedDebuffPerLevel = 0.1f;
    private float effectiveMoveSpeed;
    private float playStartTime = 0f;

    // 컴포넌트 캐싱
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite deathSprite;
    [SerializeField] private Sprite dodgeSprite;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;
    private InputAction takeOffAction;
    private InputAction dashAction;

    // 애니메이션 파라미터 해시
    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsLanding = Animator.StringToHash("IsLanding");
    private static readonly int HashIsChecking = Animator.StringToHash("IsChecking");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");
    private static readonly int HashIsDashing = Animator.StringToHash("IsDashing");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerInput = GetComponent<PlayerInput>();
        animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.updateMode = AnimatorUpdateMode.UnscaledTime; // 슬로우모션 중에도 애니메이션 프레임 유지
        }
        spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        rb.gravityScale = 0f;

        // [추가] 자식 오브젝트에서 MosquitoTailSync 자동 탐색
        if (tailSync == null)
        {
            tailSync = GetComponentInChildren<MosquitoTailSync>();
        }

        if (spriteRenderer != null) spriteRenderer.sortingOrder = 10;
        if (deathSprite == null) deathSprite = Resources.Load<Sprite>("Sprites/Mosquito_death");

        if (playerInput != null && playerInput.actions != null)
        {
            checkAction = playerInput.actions.FindAction("Check");
            suckAction = playerInput.actions.FindAction("Suck");
            takeOffAction = playerInput.actions.FindAction("TakeOff");
            dashAction = playerInput.actions.FindAction("Dash");
        }

        if (currentBlood <= 0f) currentBlood = 40f;

        EnsureBloodGauge();
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

        BloodManager.OnBloodDepleted += DieFromStarvation;
        EscapeSystem.OnGameClear += OnGameClear;
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

        BloodManager.OnBloodDepleted -= DieFromStarvation;
        EscapeSystem.OnGameClear -= OnGameClear;
        ResetTimeScale();
    }

    private void OnGameClear()
    {
        isDead = true;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        ResetTimeScale();
    }

    private void Start()
    {
        playStartTime = Time.time;

        if (BloodManager.Instance != null)
        {
            currentBlood = BloodManager.Instance.CurrentBlood > 0f ? BloodManager.Instance.CurrentBlood : 40f;
            maxBlood = BloodManager.Instance.MaxTargetBlood > 0f ? BloodManager.Instance.MaxTargetBlood : 200f;
        }
        else
        {
            currentBlood = 40f;
            maxBlood = 200f;
        }

        CalculateEffectiveSpeed();

        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);

        // [추가] 시작 프레임 시각 상태 강제 동기화
        UpdateDirection(isFacingRight, true);

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

        // [F1 치트] 혈액 200ml 즉시 충전 & 탈출구 개방
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            if (BloodManager.Instance != null)
            {
                BloodManager.Instance.SetBloodFullCheat();
                currentBlood = BloodManager.Instance.CurrentBlood;
            }
            else
            {
                currentBlood = maxBlood;
                OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
                EscapeSystem.Instance?.ActivateRandomEscapeZone();
            }
            CalculateEffectiveSpeed();
        }

        // [개선] 스프라이트 및 꼬리 위치 좌우 반전 체크
        UpdateSpriteFacing();

        if (currentState == MosquitoState.Flying)
        {
            DrainFlightBlood();
            HandleKeyboardDashCheck();
            EvaluateFlyingDanger();
        }
        else if (currentState == MosquitoState.Sucking)
        {
            HandleKeyboardDashCheck();
            if (isDashing) return;

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                PerformTakeOff();
                return;
            }

            ProcessBloodSucking();
            EvaluateSuckingDanger();
        }
        else if (currentState == MosquitoState.Checking)
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                SkillCheckUI.Instance?.OnInputPressed();
            }
        }
    }

    private void DrainFlightBlood()
    {
        if (isDead || currentState != MosquitoState.Flying) return;
        if (Time.time - playStartTime < 0.5f) return;

        float drainAmount = flightBloodDrainRate * Time.deltaTime;
        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.ConsumeBlood(drainAmount);
            currentBlood = BloodManager.Instance.CurrentBlood;
        }
        else
        {
            currentBlood = Mathf.Max(0f, currentBlood - drainAmount);
            if (currentBlood <= 0f)
            {
                DieFromStarvation();
                return;
            }
        }

        OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
        CalculateEffectiveSpeed();
    }

    /// <summary>
    /// 이동 입력값에 따라 모기 몸통 및 꼬리의 좌우 방향 반전을 제어
    /// </summary>
    private void UpdateSpriteFacing()
    {
        if (moveInput.x > 0.05f)
        {
            UpdateDirection(true);
        }
        else if (moveInput.x < -0.05f)
        {
            UpdateDirection(false);
        }
    }

    /// <summary>
    /// 모기의 방향 상태를 갱신하고 몸통(SpriteRenderer)과 꼬리(MosquitoTailSync)를 함께 동기화
    /// </summary>
    /// <param name="faceRight">오른쪽 바라봄 여부</param>
    /// <param name="forceUpdate">강제 갱신 여부 (Start 등에서 호출 시 사용)</param>
    public void UpdateDirection(bool faceRight, bool forceUpdate = false)
    {
        if (!forceUpdate && isFacingRight == faceRight) return;

        isFacingRight = faceRight;

        // 1. 몸통(Player) flipX 반전 (원본이 오른쪽을 보고 있으므로 faceRight가 false일 때 flipX = true)
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = !isFacingRight;
        }

        // 2. 자식 꼬리(Visual) 위치 및 flipX 동기화 호출
        if (tailSync != null)
        {
            tailSync.SynchronizeTail(isFacingRight);
        }
    }

    private void HandleKeyboardDashCheck()
    {
        if (isDashing) return;

        bool dashTriggered = (Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame)) ||
                             (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

        if (dashTriggered) PerformDash();
    }

    #region 대시(Dash) 및 불릿 타임

    public void OnDash(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        PerformDash();
    }

    private void OnDashInputReceived(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        PerformDash();
    }

    public void PerformDash()
    {
        if (isDead || isDashing) return;
        if (currentState != MosquitoState.Flying && currentState != MosquitoState.Sucking) return;
        if (Time.unscaledTime - lastDashTime < dashCooldown) return;

        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.ConsumeBlood(dashBloodCost);
            currentBlood = BloodManager.Instance.CurrentBlood;
        }
        else
        {
            currentBlood = Mathf.Max(0f, currentBlood - dashBloodCost);
            if (currentBlood <= 0f)
            {
                DieFromStarvation();
                return;
            }
        }

        lastDashTime = Time.unscaledTime;

        if (currentState == MosquitoState.Sucking)
        {
            FinishBiteSession();
            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }

        // [개선] isFacingRight 상태 기반으로 대시 방향 지정
        Vector2 facingDir = isFacingRight ? Vector2.right : Vector2.left;
        currentDashDirection = moveInput != Vector2.zero ? moveInput.normalized : facingDir;

        StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        isDashing = true;

        // 인스펙터 값이 1 이상이거나 비정상일 경우 안전하게 0.2f (5배 슬로우) 적용
        float effectiveSlow = (slowTimeScale > 0.01f && slowTimeScale < 0.95f) ? slowTimeScale : 0.2f;
        Time.timeScale = effectiveSlow;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        // [요청 반영] 꼬리 뒤쪽(모기 본체 크기만큼 뒤)에 모기 크기의 1.5배 크기로 닷지 이펙트 생성
        if (dodgeSprite != null)
        {
            StartCoroutine(SpawnDodgeEffectRoutine());
        }

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Dash);
        UpdateAnimationState();
        if (animator != null)
        {
            animator.Play("IsDashing", 0, 0f);
        }

        Debug.Log($"<color=yellow>[MosquitoController] ⚡ 닷지(대시) 발동! (슬로우모션 Time.timeScale = {Time.timeScale:F2}, 지속시간 = {dashDuration}s)</color>");

        yield return new WaitForSecondsRealtime(dashDuration);

        isDashing = false;
        ResetTimeScale();
        UpdateAnimationState();
    }

    /// <summary>
    /// 모기 꼬리 뒤쪽에 본체 크기만큼 뒤, 모기 실제 크기의 정확한 1.5배 크기로 dodge 잔상을 출력하고 자연스럽게 페이드아웃
    /// </summary>
    private IEnumerator SpawnDodgeEffectRoutine()
    {
        if (dodgeSprite == null) yield break;

        GameObject dodgeObj = new GameObject("DodgeEffect");
        SpriteRenderer sr = dodgeObj.AddComponent<SpriteRenderer>();
        sr.sprite = dodgeSprite;
        sr.sortingOrder = 9; // 모기 본체(10) 바로 뒤 레이어
        sr.flipX = !isFacingRight; // 모기 방향과 일치

        // 1. 모기 본체의 실제 월드 크기 산출
        Vector2 mosquitoSize = spriteRenderer != null ? (Vector2)spriteRenderer.bounds.size : new Vector2(1.5f, 1.0f);
        if (mosquitoSize.x <= 0.01f) mosquitoSize = new Vector2(1.5f, 1.0f);

        // 2. dodgeSprite 고해상도(1672x941 등) 텍스처를 고려하여 모기 본체의 1.5배 크기로 스케일 정밀 계산
        Vector2 dodgeSpriteWorldSize = dodgeSprite.rect.size / dodgeSprite.pixelsPerUnit;
        if (dodgeSpriteWorldSize.x <= 0.01f) dodgeSpriteWorldSize = Vector2.one;

        float targetWidth = mosquitoSize.x * 1.5f;
        float finalScale = targetWidth / dodgeSpriteWorldSize.x;
        dodgeObj.transform.localScale = new Vector3(finalScale, finalScale, 1f);

        // 3. 꼬리 뒤쪽 위치: 모기 본체 크기만큼 정확히 뒤쪽에 배치
        Vector3 backDirection = isFacingRight ? Vector3.left : Vector3.right;
        float offsetDistance = Mathf.Max(0.7f, mosquitoSize.x * 0.85f);
        dodgeObj.transform.position = transform.position + (backDirection * offsetDistance);

        float effectDuration = 1.0f; // [요청 반영] 1.0초 동안 유지
        float timer = 0f;
        Color initialColor = Color.white;

        while (timer < effectDuration)
        {
            timer += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(timer / effectDuration);
            
            if (sr != null)
            {
                // 초반 0.6초는 선명하게(Alpha 0.9) 유지하다가 후반부에 부드럽게 페이드아웃
                float alpha = progress < 0.6f 
                    ? Mathf.Lerp(0.95f, 0.8f, progress / 0.6f) 
                    : Mathf.Lerp(0.8f, 0f, (progress - 0.6f) / 0.4f);

                Color c = initialColor;
                c.a = alpha;
                sr.color = c;
            }
            yield return null;
        }

        if (dodgeObj != null)
        {
            Destroy(dodgeObj);
        }
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
    }

    #endregion

    #region 흡혈 및 세션 관리

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
        currentZoneSuckedBlood = 0f;
        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();
    }

    private void ProcessBloodSucking()
    {
        bool isSuckingHeld = (suckAction != null && suckAction.IsPressed()) ||
                             (Mouse.current != null && Mouse.current.leftButton.isPressed);

        if (isSuckingHeld)
        {
            float requested = suckRate * Time.deltaTime;
            if (currentZoneMaxSuckAmount > 0f)
            {
                float remainInZone = currentZoneMaxSuckAmount - currentZoneSuckedBlood;
                requested = Mathf.Min(requested, remainInZone);
            }

            float actual = BloodManager.Instance != null ?
                BloodManager.Instance.RequestSuckBlood(requested) :
                Mathf.Min(requested, maxBlood - currentBlood);

            if (BloodManager.Instance != null)
            {
                currentBlood = BloodManager.Instance.CurrentBlood;
            }
            else
            {
                currentBlood = Mathf.Min(currentBlood + actual, maxBlood);
            }

            currentZoneSuckedBlood += actual;

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
            CalculateEffectiveSpeed();

            if (currentBlood >= escapeThreshold || (BloodManager.Instance != null && BloodManager.Instance.IsEscapeReady))
            {
                EscapeSystem.Instance?.ActivateRandomEscapeZone();
            }

            if (currentBlood >= maxBlood || (BloodManager.Instance != null && BloodManager.Instance.IsFull))
            {
                OnSuckingCompleted();
            }
            else if (currentZoneMaxSuckAmount > 0f && currentZoneSuckedBlood >= currentZoneMaxSuckAmount)
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
        if (currentBitingZone != null)
        {
            if (currentZoneSuckedBlood > 0f)
            {
                currentBitingZone.RegisterBiteMark(transform.position);
                Debug.Log($"<color=green>[흡혈 성공] {currentZoneSuckedBlood:F1}ml 흡혈 완료! 물린 자국을 남깁니다.</color>");
            }
            else
            {
                Debug.Log("<color=yellow>[흡혈 미달] 피를 빨지 못하고 이륙하여 물린 자국을 생성하지 않습니다.</color>");
            }

            ClearBiteSessionData();
        }
    }

    private void AbortBiteSession()
    {
        if (currentBitingZone != null)
        {
            Debug.Log("<color=orange>[세션 취소] QTE 실패 또는 기습으로 인해 자국 없이 이륙합니다.</color>");
            ClearBiteSessionData();
        }
    }

    private void ClearBiteSessionData()
    {
        currentBitingZone = null;
        currentZoneSuckedBlood = 0f;
        currentZoneMaxSuckAmount = 0f;
    }

    private void CalculateEffectiveSpeed()
    {
        float bloodRatio = 0f;

        if (BloodManager.Instance != null)
        {
            bloodRatio = Mathf.Clamp01(BloodManager.Instance.CurrentBlood / BloodManager.Instance.MaxTargetBlood);
        }

        // $v_{effective} = v_{base} \times \max(1 - ratio \times debuff \times 3, 0.4)$
        float debuffMultiplier = 1.0f - (bloodRatio * speedDebuffPerLevel * 3f);
        effectiveMoveSpeed = baseMoveSpeed * Mathf.Max(debuffMultiplier, 0.4f);
    }

    private void EnsureBloodGauge()
    {
        if (GetComponentInChildren<MosquitoBloodGaugeUI>() == null)
        {
            var gaugeObj = new GameObject("MosquitoBloodGauge");
            gaugeObj.transform.SetParent(transform, false);
            gaugeObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            gaugeObj.AddComponent<MosquitoBloodGaugeUI>();
        }
    }

    #endregion

    #region 기습 공격 평가 & 사망/기아/리스폰 처리

    private void EvaluateFlyingDanger()
    {
        attackCheckTimer += Time.deltaTime;
        if (attackCheckTimer < checkInterval) return;
        attackCheckTimer = 0f;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit != null && hit.TryGetComponent<IBodyPartZone>(out var zone))
        {
            float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;
            if (UnityEngine.Random.value <= zone.DangerProbability * angerMult * 0.25f)
            {
                HumanAngerManager.Instance?.TriggerAttack(transform.position);
            }
        }
    }

    private void EvaluateSuckingDanger()
    {
        attackCheckTimer += Time.deltaTime;
        if (attackCheckTimer < checkInterval) return;
        attackCheckTimer = 0f;

        float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;
        bool isHolding = (suckAction != null && suckAction.IsPressed()) || (Mouse.current != null && Mouse.current.leftButton.isPressed);
        float suckRisk = isHolding ? 2.0f : 0.5f;

        if (UnityEngine.Random.value <= currentZoneDangerRatio * angerMult * 0.3f * suckRisk)
        {
            HumanAngerManager.Instance?.TriggerAttack(transform.position);
        }
    }

    public void OnHitByHumanHand()
    {
        if (isDead) return;

        ExecuteDeathSequence();
    }

    public void DieFromStarvation()
    {
        if (isDead) return;
        if (Time.time - playStartTime < 0.5f) return;

        Debug.LogError("<color=red>[모기 기아 사망] 혈액이 0에 도달하여 굶어 죽었습니다!</color>");
        ExecuteDeathSequence();
    }

    private void ExecuteDeathSequence()
    {
        if (isDead) return;

        isDead = true;
        currentState = MosquitoState.Dead;

        ResetTimeScale();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        if (playerInput != null)
            playerInput.enabled = false;

        AudioManager.Instance?.StopMosquitoBuzz();
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.GameOver);

        StopAllCoroutines();
        StartCoroutine(DeathMotionRoutine());

        OnMosquitoDied?.Invoke();
    }

    private IEnumerator DeathMotionRoutine()
    {
        if (animator != null) animator.enabled = false;
        if (spriteRenderer != null && deathSprite != null)
            spriteRenderer.sprite = deathSprite;

        Vector3 baseScale = Vector3.one;
        float timer = 0f;
        float flattenDuration = 0.15f;

        while (timer < flattenDuration)
        {
            timer += Time.deltaTime;
            float t = timer / flattenDuration;
            transform.localScale = new Vector3(
                baseScale.x * Mathf.Lerp(1.0f, 1.4f, t),
                baseScale.y * Mathf.Lerp(1.0f, 0.4f, t),
                baseScale.z
            );
            yield return null;
        }

        transform.localScale = new Vector3(baseScale.x * 1.4f, baseScale.y * 0.4f, baseScale.z);
    }

    public void RespawnMosquito(Vector3 spawnPosition)
    {
        isDead = false;
        currentState = MosquitoState.Flying;
        transform.position = spawnPosition;
        transform.localScale = Vector3.one;
        BloodManager.Instance?.ResetBlood();
        currentBlood = BloodManager.Instance != null ? BloodManager.Instance.CurrentBlood : 40f;
        playStartTime = Time.time;

        CalculateEffectiveSpeed();
        ResetTimeScale();

        if (rb != null)
        {
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
        }

        if (playerInput != null) playerInput.enabled = true;

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
    }

    public void OnLand(InputAction.CallbackContext context)
    {
        if (isDead || !context.performed || currentState != MosquitoState.Flying) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null) return;

        Vector3 landingPoint = hit.ClosestPoint(transform.position);
        BitingZone bZone = hit.GetComponent<BitingZone>() ?? hit.GetComponentInParent<BitingZone>();

        if (bZone == null)
        {
            bZone = hit.gameObject.AddComponent<BitingZone>();
            if (hit.name.Contains("Head") || (hit.transform.parent != null && hit.transform.parent.name.Contains("Head")))
                bZone.CurrentZoneType = GlobalEnums.ZoneType.Red;
            else if (hit.name.Contains("Upper") || (hit.transform.parent != null && hit.transform.parent.name.Contains("Upper")))
                bZone.CurrentZoneType = GlobalEnums.ZoneType.Yellow;
            else
                bZone.CurrentZoneType = GlobalEnums.ZoneType.Green;
        }

        if (bZone.IsPositionAlreadyBitten(landingPoint)) return;

        currentBitingZone = bZone;
        currentZoneMaxSuckAmount = bZone.GetMaxSuckAmount();

        IBodyPartZone zone = hit.GetComponent<IBodyPartZone>() ?? hit.GetComponentInParent<IBodyPartZone>();
        if (zone != null)
        {
            currentZoneDangerRatio = zone.DangerProbability;
            StartLandingSequence(hit, currentZoneDangerRatio);
        }
    }

    private void StartLandingSequence(Collider2D skin, float dangerRatio)
    {
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

        SkillCheckUI.Instance?.BeginSkillCheck(1f + dangerRatio, OnDbdSkillCheckCompleted);
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
                StartSuckingSequence();
            else
                SkillCheckUI.Instance?.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
        }
        else
        {
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteFail);
            HumanAngerManager.Instance?.TriggerAttack(transform.position);

            AbortBiteSession();

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void OnCheckInputReceived(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Checking) return;
        SkillCheckUI.Instance?.OnInputPressed();
    }

    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        PerformTakeOff();
    }

    public void PerformTakeOff()
    {
        if (isDead || currentState != MosquitoState.Sucking) return;

        FinishBiteSession();
        currentState = MosquitoState.Flying;
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;

        animator.SetBool(HashIsDashing, isDashing);
        animator.SetBool(HashIsFlying, currentState == MosquitoState.Flying && !isDashing);
        animator.SetBool(HashIsLanding, currentState == MosquitoState.Landing);
        animator.SetBool(HashIsChecking, currentState == MosquitoState.Checking);

        bool isHoldingSuck = currentState == MosquitoState.Sucking &&
                             ((suckAction != null && suckAction.IsPressed()) ||
                              (Mouse.current != null && Mouse.current.leftButton.isPressed));
        animator.SetBool(HashIsSucking, isHoldingSuck);
    }

    private void SwitchActionMapSafely(string mapName)
    {
        if (playerInput != null && playerInput.actions != null && playerInput.actions.FindActionMap(mapName) != null)
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