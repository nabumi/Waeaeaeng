using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 흡혈 150ml 이상 달성 시 인스펙터/하이어라키에 배치된 탈출 지점(Transform) 중 1곳을 무작위로 선정하여 탈출구를 활성화하는 시스템
/// (하드코딩 좌표를 제거하고 씬의 Transform 데이터만 안전하게 추적하도록 개편된 버전입니다.)
/// </summary>
public class EscapeSystem : MonoBehaviour
{
    private static EscapeSystem instance;
    private static bool isApplicationQuitting = false;

    public static bool HasInstance => instance != null && !isApplicationQuitting;

    public static EscapeSystem Instance
    {
        get
        {
            if (isApplicationQuitting) return null;

            if (instance == null)
            {
                instance = FindAnyObjectByType<EscapeSystem>();
            }
            return instance;
        }
    }

    // 승리/탈출 성공 이벤트 (GameManager, UI 등에서 구독)
    public static event Action OnGameClear;

    [Header("탈출 지점 Transform 설정 (인스펙터 할당)")]
    [Tooltip("하이어라키에 배치된 탈출 지점 Transform 목록 (비어있을 경우 부모 오브젝트 탐색)")]
    [SerializeField] private Transform[] escapePointTransforms;

    [Tooltip("탈출 지점들을 자식으로 둔 부모 Transform (선택 사항)")]
    [SerializeField] private Transform escapePointsParent;

    [Header("탈출 감지 설정")]
    [Tooltip("탈출 감지 반경 (도달 인정 거리 - m 단위)")]
    [SerializeField] private float escapeTriggerRadius = 1.5f;

    [Header("시각 연출 및 UI")]
    [SerializeField] private EscapeIndicatorUI indicatorUI;

    [Tooltip("탈출 지점에 생성될 마커 스프라이트 (미지정 시 기본 백색 원 연출)")]
    [SerializeField] private Sprite escapeMarkerSprite;

    // 내부 상태 변수
    private Vector2 activeEscapePosition;
    private bool isEscapeActive = false;
    private bool isEscaped = false;
    private Transform playerTransform;
    private GameObject visualEscapeMarker;
    private string currentEscapePointName = "";

    // 최적화를 위한 제곱 거리 변수 ($r^2$)
    private float sqrTriggerRadius;

    public Vector2 ActiveEscapePosition => activeEscapePosition;
    public bool IsEscapeActive => isEscapeActive;
    public string CurrentEscapePointName => currentEscapePointName;

    private void Awake()
    {
        isApplicationQuitting = false;

        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 제곱 거리 연산 캐싱 ($r_{\text{sqr}} = r^2$)
        sqrTriggerRadius = escapeTriggerRadius * escapeTriggerRadius;

        EnsureEscapePoints();
    }

    private void OnEnable()
    {
        BloodManager.OnFullBelly += ActivateRandomEscapeZone;

        if (BloodManager.HasInstance)
        {
            BloodManager.Instance.OnBloodAmountChanged += CheckBloodStateOnUpdate;

            // 씬 진입 시 이미 탈출 조건($V_{\text{current}} \ge 150\text{ml}$)을 만족한 상태라면 즉시 활성화
            if (BloodManager.Instance.IsEscapeReady && !isEscapeActive && !isEscaped)
            {
                ActivateRandomEscapeZone();
            }
        }
    }

    private void OnDisable()
    {
        BloodManager.OnFullBelly -= ActivateRandomEscapeZone;

        if (BloodManager.HasInstance)
        {
            BloodManager.Instance.OnBloodAmountChanged -= CheckBloodStateOnUpdate;
        }
    }

    private void Start()
    {
        if (indicatorUI == null)
        {
            indicatorUI = EscapeIndicatorUI.Instance;
        }

        FindPlayer();
    }

    private void OnApplicationQuit()
    {
        isApplicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// 실시간 혈액 변화 감시 후 150ml 도달 즉시 탈출구 개방
    /// </summary>
    private void CheckBloodStateOnUpdate(float currentBlood, float maxBlood)
    {
        if (!isEscapeActive && !isEscaped && currentBlood >= 150f)
        {
            ActivateRandomEscapeZone();
        }
    }

    /// <summary>
    /// 인스펙터 및 하이어라키의 유효한 Transform만 필터링하여 수집
    /// </summary>
    public void EnsureEscapePoints()
    {
        var validList = new List<Transform>();

        // 1. 인스펙터 배열에 등록된 Transform 중 Null이 아닌 항목 수집
        if (escapePointTransforms != null && escapePointTransforms.Length > 0)
        {
            foreach (var t in escapePointTransforms)
            {
                if (t != null) validList.Add(t);
            }
        }

        // 2. 배열이 비어있고 부모 Transform이 지정된 경우 자식 수집
        if (validList.Count == 0 && escapePointsParent != null)
        {
            for (int i = 0; i < escapePointsParent.childCount; i++)
            {
                var child = escapePointsParent.GetChild(i);
                if (child != null) validList.Add(child);
            }
        }

        // 3. 씬에서 "EscapePoints" 부모 탐색
        if (validList.Count == 0)
        {
            var foundParent = GameObject.Find("EscapePoints") ?? GameObject.Find("[EscapePoints]");
            if (foundParent != null)
            {
                escapePointsParent = foundParent.transform;
                for (int i = 0; i < foundParent.transform.childCount; i++)
                {
                    var child = foundParent.transform.GetChild(i);
                    if (child != null) validList.Add(child);
                }
            }
        }

        // 4. 유효한 Transform이 0개일 경우 디버그 에러 가이드
        if (validList.Count == 0)
        {
            Debug.LogError("<color=red>[EscapeSystem] 씬 및 인스펙터에 등록된 탈출 지점(Transform)이 전혀 없습니다! EscapePoints 오브젝트를 확인해 주세요.</color>");
            return;
        }

        escapePointTransforms = validList.ToArray();
    }

    private void FindPlayer()
    {
        if (playerTransform != null) return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        else
        {
            var mosquito = FindAnyObjectByType<MosquitoController>();
            if (mosquito != null) playerTransform = mosquito.transform;
        }
    }

    private void Update()
    {
        if (!isEscapeActive || isEscaped) return;

        if (playerTransform == null) return;

        // [최적화] sqrMagnitude를 이용한 빠른 거리 측정 연산
        // 수식: $d^2 = (x_2 - x_1)^2 + (y_2 - y_1)^2 \le r^2$
        Vector2 playerPos = playerTransform.position;
        float sqrDistance = (playerPos - activeEscapePosition).sqrMagnitude;

        if (sqrDistance <= sqrTriggerRadius)
        {
            TriggerEscapeSuccess();
        }

        // 마커 맥박(Pulse) 및 회전 효과
        if (visualEscapeMarker != null)
        {
            float pulse = 0.35f + 0.04f * Mathf.Sin(Time.time * 5f);
            visualEscapeMarker.transform.localScale = new Vector3(pulse, pulse, 1f);
            visualEscapeMarker.transform.Rotate(0f, 0f, 30f * Time.deltaTime);
        }
    }

    /// <summary>
    /// 수집된 Transform 중 1곳을 무작위로 추첨하여 탈출구로 가동
    /// </summary>
    public void ActivateRandomEscapeZone()
    {
        if (isEscapeActive || isEscaped) return;

        EnsureEscapePoints();

        if (escapePointTransforms == null || escapePointTransforms.Length == 0)
        {
            Debug.LogError("<color=red>[EscapeSystem] 활성화할 탈출 지점 Transform이 존재하지 않습니다!</color>");
            return;
        }

        // 유효한 Transform 목록 중 Random Pick
        int randomIndex = UnityEngine.Random.Range(0, escapePointTransforms.Length);
        Transform chosenTransform = escapePointTransforms[randomIndex];

        if (chosenTransform == null)
        {
            Debug.LogError("<color=red>[EscapeSystem] 선택된 탈출 지점 Transform이 Null입니다!</color>");
            return;
        }

        activeEscapePosition = chosenTransform.position;
        currentEscapePointName = chosenTransform.name;

        isEscapeActive = true;
        isEscaped = false;

        Debug.LogWarning($"<color=cyan>[탈출 개시!] 만복 달성! 선정된 탈출 지점: {currentEscapePointName} ({activeEscapePosition})</color>");

        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.EscapeReady);

        CreateVisualEscapeMarker(activeEscapePosition);

        if (indicatorUI == null) indicatorUI = EscapeIndicatorUI.Instance;
        if (indicatorUI != null)
        {
            indicatorUI.ShowIndicator(activeEscapePosition);
        }
    }

    private void CreateVisualEscapeMarker(Vector2 position)
    {
        if (visualEscapeMarker != null) Destroy(visualEscapeMarker);

        visualEscapeMarker = new GameObject("Visual_EscapeZone");
        visualEscapeMarker.transform.position = new Vector3(position.x, position.y, 0f);
        visualEscapeMarker.transform.localScale = new Vector3(0.35f, 0.35f, 1f);

        var sr = visualEscapeMarker.AddComponent<SpriteRenderer>();

        if (escapeMarkerSprite != null)
        {
            sr.sprite = escapeMarkerSprite;
        }
        else
        {
            Texture2D tex = Texture2D.whiteTexture;
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        sr.color = new Color(0.1f, 1.0f, 0.4f, 0.75f);
        sr.sortingOrder = 5;
    }

    private void TriggerEscapeSuccess()
    {
        if (isEscaped) return;
        isEscaped = true;
        isEscapeActive = false;

        if (indicatorUI != null)
        {
            indicatorUI.HideIndicator();
        }

        if (visualEscapeMarker != null)
        {
            Destroy(visualEscapeMarker);
        }

        Debug.LogWarning($"<color=green>[GAME CLEAR] 모기가 '{currentEscapePointName}' 위치로 무사히 탈출했습니다!</color>");

        BloodManager.Instance?.StopTimer();

        OnGameClear?.Invoke();
    }

    private void OnDrawGizmos()
    {
        // 에디터 씬 뷰에서 설정된 Transform 위치들을 기즈모로 시각화
        if (escapePointTransforms != null)
        {
            for (int i = 0; i < escapePointTransforms.Length; i++)
            {
                var t = escapePointTransforms[i];
                if (t == null) continue;

                Gizmos.color = isEscapeActive && (Vector2)t.position == activeEscapePosition
                    ? Color.yellow
                    : new Color(0.2f, 1f, 0.4f, 0.6f);

                Gizmos.DrawWireSphere(t.position, escapeTriggerRadius);
                Gizmos.DrawSphere(t.position, 0.2f);
            }
        }
    }
}