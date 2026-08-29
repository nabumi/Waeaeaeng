using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 흡혈 150ml 이상 달성 시 하이어라키에 배치된 8개 탈출 지점 중 1곳을 무작위로 선정하여 탈출구를 활성화하는 시스템
/// (하이어라키의 Transform을 직접 드래그앤드롭하거나 'EscapePoints' 부모 오브젝트를 통해 정교하게 위치 지정 가능)
/// </summary>
public class EscapeSystem : MonoBehaviour
{
    private static EscapeSystem instance;
    private static bool isQuitting = false;

    public static EscapeSystem Instance
    {
        get
        {
            if (isQuitting) return null;
            if (instance == null)
            {
                instance = FindAnyObjectByType<EscapeSystem>();
                if (instance == null && Application.isPlaying)
                {
                    var go = new GameObject("[EscapeSystem]");
                    instance = go.AddComponent<EscapeSystem>();
                }
            }
            return instance;
        }
    }

    // 승리/탈출 성공 이벤트 (GameManager, UI 등에서 구독)
    public static event Action OnGameClear;

    [Header("탈출 지점 Transform 설정 (하이어라키에서 지정)")]
    [Tooltip("하이어라키에 배치된 8개의 탈출 지점 Transform 목록 (비어있으면 EscapePoints 부모를 탐색하거나 자동 생성)")]
    [SerializeField] private Transform[] escapePointTransforms = new Transform[8];

    [Tooltip("8개 탈출 지점을 자식으로 둔 부모 Transform (선택 사항)")]
    [SerializeField] private Transform escapePointsParent;

    [Header("탈출 감지 설정")]
    [Tooltip("탈출 감지 반경 (도달 인정 거리)")]
    [SerializeField] private float escapeTriggerRadius = 1.5f;

    [Header("방향 지시계 UI")]
    [SerializeField] private EscapeIndicatorUI indicatorUI;

    // 기본 8방향 안전 외곽 기본 좌표 (사람 몸체를 완전히 피한 방 외곽 모서리/창문 위치)
    private static readonly Vector2[] DefaultSafeOffsets = new Vector2[]
    {
        new Vector2(0f, 4.3f),       // 1. 북 (창문/환풍구)
        new Vector2(6.5f, 4.0f),     // 2. 북동 (우측 상단 구석)
        new Vector2(7.2f, 0f),       // 3. 동 (우측 문틈)
        new Vector2(6.5f, -4.0f),    // 4. 남동 (우측 하단 구석)
        new Vector2(0f, -4.3f),      // 5. 남 (하단 침대 틈)
        new Vector2(-6.5f, -4.0f),   // 6. 남서 (좌측 하단 구석)
        new Vector2(-7.2f, 0f),      // 7. 서 (좌측 벽 틈)
        new Vector2(-6.5f, 4.0f)     // 8. 북서 (좌측 상단 구석)
    };

    private static readonly string[] PointNames = new string[]
    {
        "1_북쪽_창문", "2_북동_우측상단", "3_동쪽_문틈", "4_남동_우측하단",
        "5_남쪽_침대밑", "6_남서_좌측하단", "7_서쪽_벽틈", "8_북서_좌측상단"
    };

    private Vector2 activeEscapePosition;
    private bool isEscapeActive = false;
    private bool isEscaped = false;
    private Transform playerTransform;
    private GameObject visualEscapeMarker;
    private string currentEscapePointName = "";

    public Vector2 ActiveEscapePosition => activeEscapePosition;
    public bool IsEscapeActive => isEscapeActive;
    public string CurrentEscapePointName => currentEscapePointName;

    private void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this)
        {
            Destroy(this);
            return;
        }

        EnsureEscapePoints();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnEnable()
    {
        BloodManager.OnFullBelly += ActivateRandomEscapeZone;
    }

    private void OnDisable()
    {
        BloodManager.OnFullBelly -= ActivateRandomEscapeZone;
    }

    private void Start()
    {
        if (indicatorUI == null)
        {
            indicatorUI = EscapeIndicatorUI.Instance;
        }

        FindPlayer();
    }

    /// <summary>
    /// 하이어라키의 8개 탈출 지점 Transform 수집 및 부재 시 자동 구성
    /// </summary>
    public void EnsureEscapePoints()
    {
        var validList = new List<Transform>();

        // 1. 직접 인스펙터에 등록된 배열 검사
        if (escapePointTransforms != null && escapePointTransforms.Length > 0)
        {
            foreach (var t in escapePointTransforms)
            {
                if (t != null) validList.Add(t);
            }
        }

        // 2. 부모 오브젝트(escapePointsParent)가 지정되어 있으면 자식 수집
        if (validList.Count == 0 && escapePointsParent != null)
        {
            for (int i = 0; i < escapePointsParent.childCount; i++)
            {
                validList.Add(escapePointsParent.GetChild(i));
            }
        }

        // 3. 씬에서 "EscapePoints" 이름의 부모 오브젝트 탐색
        if (validList.Count == 0)
        {
            var foundParent = GameObject.Find("EscapePoints");
            if (foundParent == null) foundParent = GameObject.Find("[EscapePoints]");
            if (foundParent != null)
            {
                escapePointsParent = foundParent.transform;
                for (int i = 0; i < foundParent.transform.childCount; i++)
                {
                    validList.Add(foundParent.transform.GetChild(i));
                }
            }
        }

        // 4. 그래도 없으면 하이어라키에 사용자가 위치를 편집할 수 있도록 8개 지점을 자동 생성!
        if (validList.Count == 0)
        {
            var parentObj = new GameObject("EscapePoints");
            escapePointsParent = parentObj.transform;

            for (int i = 0; i < DefaultSafeOffsets.Length; i++)
            {
                var pointGo = new GameObject(PointNames[i]);
                pointGo.transform.SetParent(escapePointsParent, false);
                pointGo.transform.position = new Vector3(DefaultSafeOffsets[i].x, DefaultSafeOffsets[i].y, 0f);
                validList.Add(pointGo.transform);
            }

            Debug.LogWarning("<color=cyan>[EscapeSystem] 하이어라키에 'EscapePoints' (8개 탈출 지점)을 자동 생성했습니다. 씬 뷰에서 자유롭게 위치를 이동하실 수 있습니다.</color>");
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

        if (playerTransform == null)
        {
            FindPlayer();
            if (playerTransform == null) return;
        }

        // 플레이어 탈출 구역 도달 감지
        float dist = Vector2.Distance(playerTransform.position, activeEscapePosition);
        if (dist <= escapeTriggerRadius)
        {
            TriggerEscapeSuccess();
        }

        // 비주얼 마커 부드러운 펄스 및 회전 효과
        if (visualEscapeMarker != null)
        {
            float pulse = 0.35f + 0.04f * Mathf.Sin(Time.time * 5f);
            visualEscapeMarker.transform.localScale = new Vector3(pulse, pulse, 1f);
            visualEscapeMarker.transform.Rotate(0f, 0f, 30f * Time.deltaTime);
        }
    }

    /// <summary>
    /// 150ml 이상 흡혈 시 하이어라키의 8개 탈출 지점 중 1곳을 무작위로 선택하여 탈출구 활성화
    /// </summary>
    public void ActivateRandomEscapeZone()
    {
        if (isEscapeActive || isEscaped) return;

        EnsureEscapePoints();

        if (escapePointTransforms == null || escapePointTransforms.Length == 0)
        {
            Debug.LogError("<color=red>[EscapeSystem] 탈출 지점 Transform이 존재하지 않습니다!</color>");
            return;
        }

        int randomIndex = UnityEngine.Random.Range(0, escapePointTransforms.Length);
        Transform chosenTransform = escapePointTransforms[randomIndex];

        if (chosenTransform != null)
        {
            activeEscapePosition = chosenTransform.position;
            currentEscapePointName = chosenTransform.name;
        }
        else
        {
            activeEscapePosition = DefaultSafeOffsets[randomIndex % DefaultSafeOffsets.Length];
            currentEscapePointName = PointNames[randomIndex % PointNames.Length];
        }

        isEscapeActive = true;
        isEscaped = false;

        Debug.LogWarning("<color=cyan>==================================================</color>");
        Debug.LogWarning($"<color=cyan>[탈출 개시!] 150ml 이상 만복 달성! 탈출 지점: {currentEscapePointName} (위치: {activeEscapePosition})</color>");
        Debug.LogWarning("<color=cyan>==================================================</color>");

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
        var circleSprites = Resources.LoadAll<Sprite>("Sprites/굵은원");
        if (circleSprites != null && circleSprites.Length > 0)
        {
            sr.sprite = circleSprites[0];
        }
        else
        {
            sr.sprite = Resources.Load<Sprite>("Sprites/원_01_흰색");
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

        Debug.LogWarning("<color=green>==================================================</color>");
        Debug.LogWarning($"<color=green>[GAME CLEAR] 모기가 '{currentEscapePointName}' 탈출구를 통해 무사히 탈출했습니다!</color>");
        Debug.LogWarning("<color=green>==================================================</color>");

        BloodManager.Instance?.StopTimer();

        OnGameClear?.Invoke();
    }

    private void OnDrawGizmos()
    {
        // 씬 뷰 에디터에서 8개 탈출 지점의 위치를 초록색 구체와 라벨로 시각화
        if (escapePointTransforms != null)
        {
            for (int i = 0; i < escapePointTransforms.Length; i++)
            {
                var t = escapePointTransforms[i];
                if (t == null) continue;

                Gizmos.color = isEscapeActive && (Vector2)t.position == activeEscapePosition ? Color.yellow : new Color(0.2f, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireSphere(t.position, escapeTriggerRadius);
                Gizmos.DrawSphere(t.position, 0.2f);
            }
        }
    }
}
