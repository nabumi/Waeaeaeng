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
    public static event Action OnGameOver;
    public event Action<float, float> OnBloodAmountChanged; // (현재 피, 최대 피)

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false;

    [Header("비행 및 이동 설정")]
    [SerializeField] private float baseMoveSpeed = 5f;
    [SerializeField] private float hoverAmplitude = 0.15f;
    [SerializeField] private float hoverFrequency = 4f;

    [Header("혈액 생존 및 소모 설정 (Energy Drain)")]
    [Tooltip("비행 중 초당 혈액 자연 소모량 (ml/s)")]
    [SerializeField] private float flightBloodDrainRate = 1.5f;

    [Tooltip("대시 사용 시 1회 혈액 소모량 (ml)")]
    [SerializeField] private float dashBloodCost = 5.0f;

    [Header("대시 및 불릿 타임 설정")]
    [SerializeField] private float dashSpeedMultiplier = 2.8f;
    [SerializeField] private float dashDuration = 0.35f;
    [SerializeField] private float slowTimeScale = 0.2f;
    [SerializeField] private float dashCooldown = 0.8f;

    private bool isDashing = false;
    public bool IsDashing => isDashing;
    private float lastDashTime = -999f;
    private Vector2 currentDashDirection = Vector2.right;

    [Header("피부 안착 및 위험도 설정")]
    [SerializeField] private LayerMask humanSkinLayer;
    [SerializeField] private float landingRadius = 1.5f;
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
    private bool hasRegisteredBiteMark = false;

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
    private Vector2 moveInput;

    private InputAction landAction;
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
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;

        if (spriteRenderer != null) spriteRenderer.sortingOrder = 10;
        if (deathSprite == null) deathSprite = Resources.Load<Sprite>("Sprites/Mosquito_death");

        if (playerInput != null && playerInput.actions != null)
        {
            landAction = playerInput.actions.FindAction("Land");
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
        if (landAction != null) landAction.performed += OnLandInputReceived;
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
        if (landAction != null) landAction.performed -= OnLandInputReceived;
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

        UpdateSpriteFacing();

        if (currentState == MosquitoState.Flying)
        {
            DrainFlightBlood();
            HandleKeyboardDashCheck();
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                TryLand();
            }
            EvaluateFlyingDanger();
        }
        else if (currentState == MosquitoState.Checking)
        {
            if ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
            {
                SkillCheckUI.Instance?.OnInputPressed();
            }
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

    private void UpdateSpriteFacing()
    {
        if (moveInput.x > 0.05f && spriteRenderer != null && spriteRenderer.flipX)
            spriteRenderer.flipX = false;
        else if (moveInput.x < -0.05f && spriteRenderer != null && !spriteRenderer.flipX)
            spriteRenderer.flipX = true;
    }

    private void HandleKeyboardDashCheck()
    {
        if (isDashing) return;

        bool dashTriggered = (Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame)) ||
                             (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

        if (dashTriggered) PerformDash();
    }

    #region 대시(Dash) 및 불릿 타임

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

        Vector2 facingDir = (spriteRenderer != null && spriteRenderer.flipX) ? Vector2.left : Vector2.right;
        currentDashDirection = moveInput != Vector2.zero ? moveInput.normalized : facingDir;

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

    #region 흡혈 및 만복/탈출 연동

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
        hasRegisteredBiteMark = false;
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

            // 피를 한 번이라도 빨면 즉시 해당 위치에 물린 자국(흉터) 생성 및 등록
            if (actual > 0f && !hasRegisteredBiteMark && currentBitingZone != null)
            {
                currentBitingZone.RegisterBiteMark(transform.position);
                hasRegisteredBiteMark = true;
            }

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);
            CalculateEffectiveSpeed();

            // 1. 150ml 돌파 시 탈출 시스템 활성화
            if (currentBlood >= escapeThreshold || (BloodManager.Instance != null && BloodManager.Instance.IsEscapeReady))
            {
                EscapeSystem.Instance?.ActivateRandomEscapeZone();
            }

            // 2. 최대 혈액(200ml) 도달 시 흡혈 완료 및 이륙
            if (currentBlood >= maxBlood || (BloodManager.Instance != null && BloodManager.Instance.IsFull))
            {
                OnSuckingCompleted();
            }
            // 3. 구역 고갈 시 이륙
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
            // 혹시 아직 생성되지 않았지만 피를 빤 적이 있는 경우에만 생성
            if (!hasRegisteredBiteMark && currentZoneSuckedBlood > 0f)
            {
                currentBitingZone.RegisterBiteMark(transform.position);
            }
            currentBitingZone = null;
        }
        hasRegisteredBiteMark = false;
    }

    private void CalculateEffectiveSpeed()
    {
        float bloodFillRatio = Mathf.Clamp01(currentBlood / maxBlood);
        float debuffMultiplier = 1.0f - (bloodFillRatio * speedDebuffPerLevel * 3f);
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

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Slap);
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

        SkillCheckUI.Instance?.ForceCancelSkillCheck();
        AudioManager.Instance?.StopMosquitoBuzz();
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.GameOver);

        BloodManager.Instance?.StopTimer();

        StopAllCoroutines();
        ResetTimeScale();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        if (playerInput != null) playerInput.enabled = false;
        HumanAngerManager.Instance?.ResetAnger();

        StartCoroutine(DeathMotionRoutine());
    }

    private IEnumerator DeathMotionRoutine()
    {
        if (animator != null) animator.enabled = false;
        if (spriteRenderer != null && deathSprite != null) spriteRenderer.sprite = deathSprite;

        Vector3 baseScale = Vector3.one;
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
        yield return new WaitForSeconds(Mathf.Max(0f, 1.0f - flattenDuration));

        OnGameOver?.Invoke();
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

    private void OnLandInputReceived(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryLand();
        }
    }

    public void OnLand(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryLand();
        }
    }

    /// <summary>
    /// 플레이어 전방/반경 1.5m 이내 피부(Layer 3) 표면을 감지하여 안착 시퀀스 시작
    /// </summary>
    public bool TryLand()
    {
        if (isDead || currentState != MosquitoState.Flying) return false;

        // 1. 사람 피부 레이어(Layer 3) 또는 접촉 콜라이더 탐색
        Collider2D hit = Physics2D.OverlapCircle(transform.position, landingRadius, humanSkinLayer);
        if (hit == null)
        {
            // LayerMask 인스펙터 불일치 대비: 전체 반경 콜라이더 탐색 폴백
            var allHits = Physics2D.OverlapCircleAll(transform.position, landingRadius);
            foreach (var col in allHits)
            {
                if (col.gameObject.layer == 3 || col.name.Contains("Zone_") || col.name.Contains("Enemy_") || col.GetComponent<IBodyPartZone>() != null)
                {
                    hit = col;
                    break;
                }
            }
        }

        if (hit == null) return false;

        Vector3 landingPoint = hit.ClosestPoint(transform.position);

        // 2. BitingZone 탐색 및 부착
        BitingZone bZone = hit.GetComponent<BitingZone>();
        if (bZone == null) bZone = hit.GetComponentInParent<BitingZone>();

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

        if (bZone.IsPositionAlreadyBitten(landingPoint))
        {
            Debug.Log("<color=yellow>[MosquitoController] 이미 물린 자국 근처에는 다시 앉을 수 없습니다.</color>");
            return false;
        }

        currentBitingZone = bZone;
        currentZoneMaxSuckAmount = bZone.GetMaxSuckAmount();

        // 3. 위험도 비율 계산 (기본값 안전 보장)
        IBodyPartZone zone = hit.GetComponent<IBodyPartZone>();
        if (zone == null) zone = hit.GetComponentInParent<IBodyPartZone>();

        if (zone != null)
        {
            currentZoneDangerRatio = zone.DangerProbability;
        }
        else
        {
            if (hit.name.Contains("Head") || (hit.transform.parent != null && hit.transform.parent.name.Contains("Head")))
                currentZoneDangerRatio = 0.8f;
            else if (hit.name.Contains("Upper") || (hit.transform.parent != null && hit.transform.parent.name.Contains("Upper")))
                currentZoneDangerRatio = 0.5f;
            else
                currentZoneDangerRatio = 0.2f;
        }

        // 4. 안착 시퀀스 시작 (무조건 실행)
        StartLandingSequence(hit, currentZoneDangerRatio);
        return true;
    }

    private void StartLandingSequence(Collider2D skin, float dangerRatio)
    {
        currentState = MosquitoState.Landing;
        currentZoneSuckedBlood = 0f;
        hasRegisteredBiteMark = false;
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

            FinishBiteSession();
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