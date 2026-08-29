using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모기의 현재 행동 상태 정의 (4단계 핵심 루프)
/// </summary>
public enum MosquitoState
{
    Flying,     // 1단계: 공중 비행 및 이동
    Landing,    // 2단계: 피부에 척 달라붙는 안착 모션
    Checking,   // 3단계: 빨대를 꽂고 QTE 스킬체크 진행
    Sucking,    // 4단계: 좌클릭을 꾹 눌러 피를 빠는 상태
    Stunned,    // 피격 스턴 (예외 상태)
    Dead        // 사망 (게임 오버)
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    // =========================================================================
    // [게임오버 이벤트] 결과창 UI 스크립트가 완성되면 이 이벤트에 구독하세요!
    // =========================================================================
    public static event Action OnGameOver;

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false; // 사망/게임오버 플래그

    [Header("비행 및 부유 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float hoverAmplitude = 0.15f;
    [SerializeField] private float hoverFrequency = 4f;

    [Header("대쉬(회피) 설정")]
    [Tooltip("대쉬 시 이동 속도 배율")]
    [SerializeField] private float dashSpeedMultiplier = 2.6f;
    [Tooltip("대쉬 지속 시간 (초)")]
    [SerializeField] private float dashDuration = 0.16f;
    [Tooltip("대쉬 재사용 대기 시간 (초)")]
    [SerializeField] private float dashCooldown = 0.7f;

    private bool isDashing = false;
    public bool IsDashing => isDashing;
    private float lastDashTime = -99f;
    private Vector2 lastMoveDirection = Vector2.up;
    private Vector2 currentDashVelocity = Vector2.zero;

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
    [SerializeField] private float suckRate = 25f; // 초당 흡혈량

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
    private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite deathSprite;
    private Vector2 moveInput;

    private InputAction checkAction;
    private InputAction suckAction;      // 좌클릭 흡혈 액션
    private InputAction takeOffAction;   // 이륙 액션

    // 애니메이션 해시 키값 최적화 캐싱
    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsLanding = Animator.StringToHash("IsLanding");
    private static readonly int HashIsChecking = Animator.StringToHash("IsChecking");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");

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
        AudioManager.Instance?.StartMosquitoBuzz();
    }

    private void FixedUpdate()
    {
        if (isDead || currentState == MosquitoState.Dead) return;

        if (isDashing)
        {
            rb.linearVelocity = currentDashVelocity;
            return;
        }

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
        if (isDead || currentState == MosquitoState.Dead) return;

        if (currentState == MosquitoState.Flying)
        {
            HandleDashInput();
            EvaluateFlyingLingerAttack();
        }
        else if (currentState == MosquitoState.Sucking)
        {
            // [4단계 흡혈] Sucking 상태이면서 좌클릭을 누르고 있을 때 피를 빤다!
            ProcessBloodSucking();
            EvaluateSuckingDangerAttack();
        }
    }

    #region 4단계 흡혈 및 QTE 연산

    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;

        UpdateAnimationState();
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.BloodSuck);
        Debug.Log("<color=red>[흡혈 중...] 좌클릭 홀드: 피를 빨기 시작합니다!</color>");
    }

    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Sucking) return;

        UpdateAnimationState();
        Debug.Log("<color=yellow>[흡혈 일시정지] 좌클릭 해제: 빨대를 꽂은 채 멈춥니다.</color>");
    }

    /// <summary>
    /// QTE 성공 시 진입하는 4단계(Sucking) 시퀀스
    /// </summary>
    private void StartSuckingSequence()
    {
        if (isDead) return;

        currentState = MosquitoState.Sucking;
        feedingTimer = 0f;

        SwitchActionMapSafely("Feeding"); // 인풋 액션맵 유지
        UpdateAnimationState();

        Debug.Log("<color=green>[흡혈 준비 완료] 좌클릭을 꾹 눌러 피를 빠세요!</color>");
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
        Debug.Log("<color=cyan>[흡혈 완수!] 피를 최대로 채워 자동으로 이륙합니다.</color>");
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

        // 좌클릭으로 흡혈 중일 때 사람이 눈치챌 확률이 배가 됨
        float suckRiskMultiplier = (suckAction != null && suckAction.IsPressed()) ? 2.0f : 0.5f;
        float attackProb = currentZoneDangerRatio * angerMult * 0.3f * suckRiskMultiplier;

        if (UnityEngine.Random.value <= attackProb)
        {
            Debug.LogWarning("<color=red>[위협 감지] 흡혈 통증으로 사람이 손을 내리칩니다!</color>");
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
        // 1. 애니메이터 비활성화하여 사망 스프라이트가 프레임별로 덮어씌워지지 않도록 방지
        if (animator != null) animator.enabled = false;

        // 2. 사망 스프라이트 교체
        if (spriteRenderer != null && deathSprite != null)
        {
            spriteRenderer.sprite = deathSprite;
        }

        // 3. 타격 피격 압축 연출 (0.0s ~ 0.2s)
        Vector3 baseScale = transform.localScale;
        float timer = 0f;
        while (timer < 0.2f)
        {
            timer += Time.deltaTime;
            float t = timer / 0.2f;
            transform.localScale = new Vector3(baseScale.x * Mathf.Lerp(1.0f, 1.4f, t), baseScale.y * Mathf.Lerp(1.0f, 0.4f, t), baseScale.z);
            yield return null;
        }

        // 4. 빙글빙글 회전하며 낙하 연출 (0.2s ~ 1.0s)
        timer = 0f;
        float fallDuration = 0.8f;
        Vector3 startPos = transform.position;
        while (timer < fallDuration)
        {
            timer += Time.deltaTime;
            float t = timer / fallDuration;

            // 회전 및 아래로 낙하
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 720f, t));
            transform.position = startPos + new Vector3(0f, -Mathf.Sin(t * Mathf.PI * 0.5f) * 0.8f, 0f);

            yield return null;
        }

        // 5. 정확히 1.0초 사망 모션 완료 후 게임오버 이벤트 발화
        Debug.LogError("<color=yellow>[GAME OVER] 1.0초 사망 모션 완료 -> 게임오버 결과창 페이드인</color>");
        OnGameOver?.Invoke();
    }

    public void RespawnMosquito(Vector3 spawnPosition)
    {
        isDead = false;
        currentState = MosquitoState.Flying;
        transform.position = spawnPosition;
        currentBlood = 0f;

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

        Debug.Log("<color=green>[부활/리셋] 새로운 모기가 피를 찾아 날아오릅니다!</color>");
    }

    #endregion

    #region Input & QTE Handlers

    public void OnMove(InputAction.CallbackContext context)
    {
        if (isDead) return;
        moveInput = context.ReadValue<Vector2>();
        if (moveInput != Vector2.zero)
        {
            lastMoveDirection = moveInput.normalized;
        }
    }

    private void HandleDashInput()
    {
        if (isDashing) return;

        // Shift 키 또는 마우스 우클릭 입력 감지
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
            TryPerformDash();
        }
    }

    public void TryPerformDash()
    {
        if (isDead || currentState != MosquitoState.Flying || isDashing) return;
        if (Time.time < lastDashTime + dashCooldown) return;

        Vector2 dashDir = moveInput != Vector2.zero ? moveInput.normalized : (lastMoveDirection != Vector2.zero ? lastMoveDirection : Vector2.up);
        StartCoroutine(DashRoutine(dashDir));
    }

    private IEnumerator DashRoutine(Vector2 direction)
    {
        isDashing = true;
        lastDashTime = Time.time;
        currentDashVelocity = direction * (moveSpeed * dashSpeedMultiplier);

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Dash);

        // 순간적인 스프라이트 스트레치 연출
        Vector3 originalScale = transform.localScale;
        transform.localScale = new Vector3(originalScale.x * 1.3f, originalScale.y * 0.8f, originalScale.z);

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            if (isDead || currentState != MosquitoState.Flying) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localScale = originalScale;
        currentDashVelocity = Vector2.zero;
        isDashing = false;
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

        // 2단계: Landing 상태 진입
        currentState = MosquitoState.Landing;
        transform.position = skin.ClosestPoint(transform.position);

        SwitchActionMapSafely("SkillCheck");
        UpdateAnimationState();

        StartCoroutine(WaitLandingAnimationRoutine(dangerRatio));
    }

    private IEnumerator WaitLandingAnimationRoutine(float dangerRatio)
    {
        // 안착 모션이 재생될 시간 동안 대기
        yield return new WaitForSeconds(0.3f);

        if (isDead) yield break;

        // 3단계: Checking(QTE) 상태로 전환
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
            StartSuckingSequence(); // 성공 시 4단계(Sucking)로 직행
        }
        else if (result == SkillCheckUI.SkillCheckResult.Success)
        {
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteSuccess);
            currentSkillCheckCount++;

            if (currentSkillCheckCount >= requiredSkillChecks)
            {
                StartSuckingSequence(); // 요구 횟수 채우면 4단계(Sucking)로 진입
            }
            else
            {
                SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
            }
        }
        else // Fail (QTE 실패 시 손바닥 공격 및 비행으로 복귀)
        {
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.QteFail);
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
            Debug.Log("<color=yellow>[강제 이륙] 플레이어가 수동으로 이륙했습니다.</color>");
        }
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;

        // 4가지 핵심 상태와 애니메이터 불리언 파라미터를 1대1로 정확히 동기화
        animator.SetBool(HashIsFlying, currentState == MosquitoState.Flying);
        animator.SetBool(HashIsLanding, currentState == MosquitoState.Landing);
        animator.SetBool(HashIsChecking, currentState == MosquitoState.Checking);

        // Sucking 상태이면서 실제 좌클릭을 누르고 있을 때만 IsSucking 애니메이션 불이 들어오도록 처리
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
    }
}