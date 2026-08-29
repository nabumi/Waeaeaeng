using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모기의 현재 행동 상태 정의
/// </summary>
public enum MosquitoState
{
    Flying,          // 공중 비행
    SkillChecking,   // QTE 스킬체크 진행 중
    Feeding,         // 피부 안착 및 흡혈 가능 상태
    Stunned,         // 손바닥 피격 스턴
    Dead             // 피격 사망 (게임 오버)
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    // =========================================================================
    // [게임오버 이벤트] 나중에 결과창 UI 스크립트가 완성되면 이 이벤트에 구독만 하면 됩니다!
    // =========================================================================
    public static event Action OnGameOver;

    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;
    [SerializeField] private bool isDead = false; // 사망/게임오버 플래그

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
    [SerializeField] private float suckRate = 25f; // 초당 흡혈량

    // 좌클릭 누름 유무를 판별하는 플래그
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
        // 사망 상태일 경우 물리 이동 완전 차단
        if (isDead || currentState == MosquitoState.Dead) return;

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
        // 사망 상태일 경우 로직 업데이트 차단
        if (isDead || currentState == MosquitoState.Dead) return;

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

    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Feeding) return;

        isSucking = true;
        UpdateAnimationState();
        Debug.Log("<color=red>[흡혈 중...] 좌클릭 홀드: 피를 빨기 시작합니다!</color>");
    }

    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (isDead || currentState != MosquitoState.Feeding) return;

        isSucking = false;
        UpdateAnimationState();
        Debug.Log("<color=yellow>[흡혈 중단] 좌클릭 해제: 흡혈을 일시 멈춥니다.</color>");
    }

    private void StartFeedingSequence()
    {
        if (isDead) return;

        currentState = MosquitoState.Feeding;
        feedingTimer = 0f;
        isSucking = false;

        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();

        Debug.Log("<color=green>[안착 완료] 좌클릭을 꾹 눌러 피를 빠세요!</color>");
    }

    private void ProcessBloodSucking()
    {
        if (currentBlood < maxBlood)
        {
            // $B(t + \Delta t) = \min(B_{\max}, B(t) + R_{\text{suck}} \cdot \Delta t)$
            currentBlood += suckRate * Time.deltaTime;
            currentBlood = Mathf.Min(currentBlood, maxBlood);

            OnBloodAmountChanged?.Invoke(currentBlood, maxBlood);

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

    private void EvaluateFeedingDangerAttack()
    {
        feedingTimer += Time.deltaTime;
        if (feedingTimer < feedingCheckInterval) return;
        feedingTimer = 0f;

        float angerMult = HumanAngerManager.Instance != null ? HumanAngerManager.Instance.CurrentAngerMultiplier : 1f;

        float suckRiskMultiplier = isSucking ? 2.0f : 0.5f;
        float attackProb = currentZoneDangerRatio * angerMult * 0.3f * suckRiskMultiplier;

        if (UnityEngine.Random.value <= attackProb)
        {
            Debug.LogWarning("<color=red>[위협 감지] 흡혈 통증으로 사람이 손을 내리칩니다!</color>");
            HumanAngerManager.Instance?.TriggerAttack(transform.position);
        }
    }

    /// <summary>
    /// HumanAngerManager에서 손바닥 타격 성공 시 호출되는 피격/사망/게임오버 처리 함수
    /// </summary>
    public void OnHitByHumanHand()
    {
        if (isDead) return;

        isDead = true;
        currentState = MosquitoState.Dead;
        isSucking = false;

        // 1. 모든 코루틴 즉시 중단
        StopAllCoroutines();

        // 2. 물리적 이동 완전 정지 ($\vec{v} = \vec{0}$)
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false; // 물리 연산 비활성화
        }

        // 3. 플레이어 입력 차단
        if (playerInput != null)
        {
            playerInput.enabled = false;
        }

        // 4. 애니메이션 상태 갱신
        UpdateAnimationState();

        // 5. 게임오버 로그 출력
        Debug.LogError("<color=red>========================================</color>");
        Debug.LogError("<color=red>[GAME OVER] 모기가 사람 손바닥에 짓눌려 사망했습니다!</color>");
        Debug.LogError("<color=red>========================================</color>");

        // 6. 사람의 분노 수치 및 스택 초기화
        if (HumanAngerManager.Instance != null)
        {
            HumanAngerManager.Instance.ResetAnger();
        }

        // 7. 게임오버 브로드캐스트 이벤트 발행 (결과창 UI 구독용)
        OnGameOver?.Invoke();
    }

    /// <summary>
    /// [개발용 테스트 / 부활 함수] 필요 시 호출하여 플레이어를 다시 세팅
    /// </summary>
    public void RespawnMosquito(Vector3 spawnPosition)
    {
        isDead = false;
        currentState = MosquitoState.Flying;
        transform.position = spawnPosition;
        currentBlood = 0f;
        isSucking = false;

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
            StartSkillCheckSequence(hit, currentZoneDangerRatio);
        }
    }

    private void StartSkillCheckSequence(Collider2D skin, float dangerRatio)
    {
        if (isDead) return;

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
        if (isDead) return;

        if (result == SkillCheckUI.SkillCheckResult.GreatSuccess)
        {
            StartFeedingSequence();
        }
        else if (result == SkillCheckUI.SkillCheckResult.Success)
        {
            currentSkillCheckCount++;

            if (currentSkillCheckCount >= requiredSkillChecks)
            {
                StartFeedingSequence();
            }
            else
            {
                SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnDbdSkillCheckCompleted);
            }
        }
        else // Fail (QTE 실패 시 손바닥 공격 트리거)
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

        if (currentState == MosquitoState.SkillChecking && SkillCheckUI.Instance != null)
            SkillCheckUI.Instance.OnInputPressed();
    }

    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        if (isDead) return;

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