using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모기 캐릭터의 상태 정의
/// </summary>
public enum MosquitoState
{
    Flying,          // 공중 비행 (WASD 이동 가능)
    SkillChecking,   // 안착 후 침 침투 타이밍 체크 (UI QTE 진행 중)
    Feeding          // 흡혈 중 (LMB 홀드)
}

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class MosquitoController : MonoBehaviour
{
    [Header("현재 상태")]
    [SerializeField] private MosquitoState currentState = MosquitoState.Flying;

    [Header("비행 및 부유(Hovering) 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float hoverAmplitude = 0.15f; // 진폭 ($A$)
    [SerializeField] private float hoverFrequency = 4f;    // 진동수 ($\omega$)

    [Header("피부 안착 감지 설정")]
    [SerializeField] private LayerMask humanSkinLayer;    // 사람 피부 레이어
    [SerializeField] private float landingRadius = 0.8f;   // 감지 반경 ($r_{\text{landing}}$)

    [Header("스킬 체크 & 흡혈 설정")]
    [SerializeField] private int requiredSkillChecks = 2; // 흡혈 진입에 필요한 성공 횟수
    private int currentSkillCheckCount = 0;

    [SerializeField] private float maxBlood = 100f;
    private float currentBlood = 0f;
    [SerializeField] private float suckRate = 20f;        // 초당 흡혈량
    private bool isSucking = false;

    // 컴포넌트 캐싱
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private Animator animator; // 애니메이션 연동을 위한 캐싱
    private Vector2 moveInput;

    // Direct Action 참조 (인스펙터 이벤트 연결 누락 예외 원천 차단)
    private InputAction checkAction;
    private InputAction suckAction;
    private InputAction takeOffAction;

    // 애니메이터 파라미터 해시 캐싱 (문자열 탐색 최적화 및 GC 방지)
    private static readonly int HashIsFlying = Animator.StringToHash("IsFlying");
    private static readonly int HashIsFeeding = Animator.StringToHash("IsFeeding");
    private static readonly int HashIsSucking = Animator.StringToHash("IsSucking");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerInput = GetComponent<PlayerInput>();
        animator = GetComponentInChildren<Animator>(); // 자식 오브젝트의 Animator 탐색

        rb.gravityScale = 0f; // 2D 비행 게임이므로 중력 차단

        // [하드닝] Input System 액션을 C# 코드 레벨에서 직접 캐싱
        if (playerInput != null && playerInput.actions != null)
        {
            checkAction = playerInput.actions.FindAction("Check");
            suckAction = playerInput.actions.FindAction("Suck");
            takeOffAction = playerInput.actions.FindAction("TakeOff");
        }
    }

    private void OnEnable()
    {
        // C# 직접 이벤트 바인딩 (Invoke Unity Events / Send Messages 설정 차이 예외 차단)
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
        // 메모리 누수 방지를 위한 이벤트 해제
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
        // 초기 시작 상태 및 애니메이션 세팅
        SwitchActionMapSafely("Flying");
        UpdateAnimationState();
    }

    private void FixedUpdate()
    {
        // 1. 비행 상태일 때의 물리 이동 및 부유 연산
        if (currentState == MosquitoState.Flying)
        {
            Vector2 targetVelocity = moveInput * moveSpeed;

            // 정지 상태일 때 상하 부유(Hovering) 물리 속도 연산
            // $v_y(t) = A \cdot \omega \cdot \cos(\omega \cdot t)$
            if (moveInput == Vector2.zero)
            {
                float hoverVy = hoverAmplitude * hoverFrequency * Mathf.Cos(Time.fixedTime * hoverFrequency);
                targetVelocity.y = hoverVy;
            }

            // [Unity 6 최신 규격] linearVelocity 속성 사용
            rb.linearVelocity = targetVelocity;
        }
        else
        {
            // 안착, 스킬체크, 흡혈 상태에서는 완벽 정지
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void Update()
    {
        // 흡혈 연산 (Frame-rate 독립적인 deltaTime 사용)
        // $B(t) = \min(B_{\max}, B(t-\Delta t) + r_{\text{suck}} \cdot \Delta t)$
        if (currentState == MosquitoState.Feeding && isSucking)
        {
            ProcessBloodSucking();
        }
    }

    #region Input Action Handlers

    // Flying Map - WASD 이동
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    // Flying Map - 피부 안착 시도 (Space 키)
    public void OnLand(InputAction.CallbackContext context)
    {
        if (!context.performed || currentState != MosquitoState.Flying) return;

        // 착지 지점 주변의 모든 2D 콜라이더 탐색
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, landingRadius, humanSkinLayer);

        if (hitColliders.Length == 0)
        {
            string layerName = humanSkinLayer.value == 0 ? "Nothing(미지정)" : "설정된 LayerMask";
            Debug.LogWarning($"<color=yellow>[진단 실패] 반경 {landingRadius}m 내에 감지된 콜라이더가 없습니다.\n" +
                             $"1) Human Skin Layer 설정 확인 ({layerName})\n" +
                             $"2) 사람 오브젝트 Layer 확인\n" +
                             $"3) Landing Radius 수치를 늘려보세요.</color>");
            return;
        }

        IBodyPartZone detectedZone = null;
        Collider2D targetCollider = null;
        float highestDanger = -1f;

        foreach (var col in hitColliders)
        {
            IBodyPartZone zone = col.GetComponent<IBodyPartZone>();
            if (zone == null) zone = col.GetComponentInParent<IBodyPartZone>();
            if (zone == null) zone = col.GetComponentInChildren<IBodyPartZone>();

            if (zone != null)
            {
                if (zone.DangerProbability > highestDanger)
                {
                    highestDanger = zone.DangerProbability;
                    detectedZone = zone;
                    targetCollider = col;
                }
            }
        }

        if (detectedZone != null && targetCollider != null)
        {
            Debug.Log($"<color=cyan>[안착 성공] 부위: {detectedZone.PartType} | 위협 확률: {detectedZone.DangerProbability * 100f}%</color>");
            StartSkillCheckSequence(targetCollider, detectedZone.DangerProbability);
        }
        else
        {
            Debug.LogWarning("[시스템] 감지된 콜라이더 중 유효한 IBodyPartZone을 구현한 오브젝트가 없습니다.");
        }
    }

    // SkillCheck Map - Space 키 (Direct Event)
    private void OnCheckInputReceived(InputAction.CallbackContext context)
    {
        if (currentState == MosquitoState.SkillChecking)
        {
            if (SkillCheckUI.Instance != null)
            {
                SkillCheckUI.Instance.OnInputPressed();
            }
        }
    }

    // Feeding Map - 마우스 좌클릭(LMB) 누름 (Direct Event)
    private void OnSuckStarted(InputAction.CallbackContext context)
    {
        if (currentState != MosquitoState.Feeding) return;

        isSucking = true;
        if (animator != null) animator.SetBool(HashIsSucking, true);
        Debug.Log("<color=red>[흡혈 시작] 마우스를 눌러 피를 빠는 중...</color>");
    }

    // Feeding Map - 마우스 좌클릭(LMB) 뗌 (Direct Event)
    private void OnSuckCanceled(InputAction.CallbackContext context)
    {
        if (currentState != MosquitoState.Feeding) return;

        isSucking = false;
        if (animator != null) animator.SetBool(HashIsSucking, false);
        Debug.Log("<color=orange>[흡혈 일시정지] 마우스를 뗐습니다. (이륙 준비 완료)</color>");
    }

    // Feeding Map - 이륙 시도 Space 키 (Direct Event)
    private void OnTakeOffInputReceived(InputAction.CallbackContext context)
    {
        if (currentState != MosquitoState.Feeding) return;

        Debug.Log("<color=yellow>[이륙 입력 감지] Space 키가 눌렸습니다.</color>");

        // 마우스 좌클릭을 뗀 상태(isSucking == false)에서만 날아오르기 가능!
        if (!isSucking)
        {
            Debug.Log("<color=green>[이륙 성공] 공중 비행(Flying) 상태로 전환합니다!</color>");
            currentState = MosquitoState.Flying;

            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
        else
        {
            Debug.LogWarning("<color=red>[이륙 실패] 마우스 좌클릭을 누르고 있는 동안에는 이륙할 수 없습니다!</color>");
        }
    }

    #endregion

    #region State Transition Helpers

    private void StartSkillCheckSequence(Collider2D skin, float dangerRatio)
    {
        currentState = MosquitoState.SkillChecking;
        currentSkillCheckCount = 0;

        // 모기를 피부 표면에 착 붙이기
        transform.position = skin.ClosestPoint(transform.position);

        SwitchActionMapSafely("SkillCheck");
        UpdateAnimationState();

        if (SkillCheckUI.Instance != null)
        {
            SkillCheckUI.Instance.BeginSkillCheck(1f + dangerRatio, OnSkillCheckCompleted);
        }
        else
        {
            Debug.LogError("<color=red>[오류] 씬에 SkillCheckUI 스크립트가 존재하지 않습니다!</color>");
        }
    }

    private void OnSkillCheckCompleted(bool isSuccess)
    {
        if (isSuccess)
        {
            currentSkillCheckCount++;
            Debug.Log($"<color=green>[스킬체크 성공!] ({currentSkillCheckCount}/{requiredSkillChecks})</color>");

            if (currentSkillCheckCount >= requiredSkillChecks)
            {
                StartFeedingSequence();
            }
            else
            {
                if (SkillCheckUI.Instance != null)
                {
                    SkillCheckUI.Instance.BeginSkillCheck(1.3f, OnSkillCheckCompleted);
                }
            }
        }
        else
        {
            Debug.LogWarning("<color=red>[스킬체크 실패!] 사람에게 들켜 도망칩니다.</color>");

            EvaluateHumanAttack(1.0f);

            currentState = MosquitoState.Flying;
            SwitchActionMapSafely("Flying");
            UpdateAnimationState();
        }
    }

    private void StartFeedingSequence()
    {
        currentState = MosquitoState.Feeding;

        SwitchActionMapSafely("Feeding");
        UpdateAnimationState();

        Debug.Log("<color=cyan>[시스템] 침 투입 성공! 마우스 좌클릭으로 흡혈 후, 마우스를 떼고 Space 키로 이륙하세요.</color>");
    }

    private void ProcessBloodSucking()
    {
        if (currentBlood < maxBlood)
        {
            currentBlood += suckRate * Time.deltaTime;
            currentBlood = Mathf.Min(currentBlood, maxBlood);
            Debug.Log($"[흡혈 중...] ({currentBlood:F1} / {maxBlood})");
        }
    }

    private void EvaluateHumanAttack(float dangerRate)
    {
        float roll = Random.value;
        if (roll <= dangerRate)
        {
            Debug.LogWarning($"<color=red>[위험!] 사람에게 들겼습니다! (Roll: {roll:F2} <= Danger: {dangerRate:F2}) -> 찰싹 공격 개시!</color>");
        }
        else
        {
            Debug.Log($"<color=green>[안전] 적이 눈치채지 못했습니다. (Roll: {roll:F2} > Danger: {dangerRate:F2})</color>");
        }
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;

        bool isFlying = (currentState == MosquitoState.Flying);
        bool isFeeding = (currentState == MosquitoState.Feeding);

        animator.SetBool(HashIsFlying, isFlying);
        animator.SetBool(HashIsFeeding, isFeeding);

        if (!isFeeding)
        {
            isSucking = false;
            animator.SetBool(HashIsSucking, false);
        }
    }

    private void SwitchActionMapSafely(string mapName)
    {
        if (playerInput != null)
        {
            InputActionMap targetMap = playerInput.actions.FindActionMap(mapName);
            if (targetMap != null)
            {
                playerInput.SwitchCurrentActionMap(mapName);
            }
            else
            {
                Debug.LogError($"<color=red>[Input Error] '{mapName}' 이름의 Action Map을 찾을 수 없습니다!</color>");
            }
        }
    }

    #endregion

    #region Visual Debugging

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, landingRadius);
    }

    #endregion
}