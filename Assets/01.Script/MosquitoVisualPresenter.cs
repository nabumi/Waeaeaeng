using System;
using UnityEngine;

/// <summary>
/// 모기의 흡혈 상태(혈액량)에 따라 꼬리 스프라이트를 3단계(각 5칸)로 연출하는 비주얼 프레젠터
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class MosquitoVisualPresenter : MonoBehaviour
{
    [System.Serializable]
    public struct BloodStageData
    {
        [Tooltip("해당 단계 이름 (예: 1단계 - 기본 꼬리)")]
        public string stageName;

        [Tooltip("해당 단계의 5칸 채움 스프라이트 (원소 수 5개)")]
        public Sprite[] stepSprites;
    }

    [Header("스프라이트 데이터 설정 (총 3단계 x 5칸)")]
    [SerializeField] private Sprite defaultEmptySprite; // 피가 0ml일 때의 기본 스프라이트
    [SerializeField] private BloodStageData[] stageDataArray = new BloodStageData[3];

    private SpriteRenderer spriteRenderer;

    private const int STEPS_PER_STAGE = 5;
    private const int TOTAL_STAGES = 3;
    private const int TOTAL_STEPS = TOTAL_STAGES * STEPS_PER_STAGE; // 15단계

    private void Awake()
    {
        // SpriteRenderer 컴포넌트 캐싱 (GC 방지)
        spriteRenderer = GetComponent<SpriteRenderer>();

        ValidateSpriteSetup();
    }

    private void OnEnable()
    {
        // BloodManager 이벤트 구독
        if (BloodManager.HasInstance)
        {
            BloodManager.Instance.OnBloodAmountChanged += UpdateMosquitoBloodVisual;
            // 초기 상태 반영
            UpdateMosquitoBloodVisual(BloodManager.Instance.CurrentBlood, BloodManager.Instance.MaxTargetBlood);
        }
    }

    private void OnDisable()
    {
        // 이벤트 해제 (메모리 누수 방지)
        if (BloodManager.HasInstance)
        {
            BloodManager.Instance.OnBloodAmountChanged -= UpdateMosquitoBloodVisual;
        }
    }

    /// <summary>
    /// 혈액량 변화에 맞춰 스프라이트를 계산하고 교체하는 핵심 로직
    /// </summary>
    /// <param name="currentBlood">현재 혈액량</param>
    /// <param name="maxBlood">최대 혈액량 (200ml)</param>
    public void UpdateMosquitoBloodVisual(float currentBlood, float maxBlood)
    {
        // 1. 피가 0 이하일 경우 기본 스프라이트 처리
        if (currentBlood <= 0f)
        {
            if (defaultEmptySprite != null)
            {
                spriteRenderer.sprite = defaultEmptySprite;
            }
            return;
        }

        // 2. 혈액 비율 계산 (0.0 ~ 1.0)
        float bloodRatio = Mathf.Clamp01(currentBlood / maxBlood);

        // 3. 글로벌 인덱스 산출 (0 ~ 14)
        int globalStepIndex = Mathf.Clamp(Mathf.FloorToInt(bloodRatio * TOTAL_STEPS), 0, TOTAL_STEPS - 1);

        // 4. Stage 인덱스(0~2) 및 Step 인덱스(0~4) 분해
        int stageIndex = globalStepIndex / STEPS_PER_STAGE;
        int stepIndex = globalStepIndex % STEPS_PER_STAGE;

        // 5. 안전한 범위 내에서 스프라이트 교체
        if (stageIndex < stageDataArray.Length && stepIndex < stageDataArray[stageIndex].stepSprites.Length)
        {
            Sprite targetSprite = stageDataArray[stageIndex].stepSprites[stepIndex];
            if (targetSprite != null)
            {
                spriteRenderer.sprite = targetSprite;
            }
        }
    }

    /// <summary>
    /// 에디터 설정 및 인스펙터 예외 방지를 위한 검증 로직
    /// </summary>
    private void ValidateSpriteSetup()
    {
        if (stageDataArray == null || stageDataArray.Length != TOTAL_STAGES)
        {
            Debug.LogWarning("<color=yellow>[MosquitoVisualPresenter] Stage 데이터 배열 크기가 3이 아닙니다.</color>");
            return;
        }

        for (int i = 0; i < stageDataArray.Length; i++)
        {
            if (stageDataArray[i].stepSprites == null || stageDataArray[i].stepSprites.Length != STEPS_PER_STAGE)
            {
                Debug.LogWarning($"<color=yellow>[MosquitoVisualPresenter] Stage {i + 1}의 스프라이트 개수가 5개가 아닙니다!</color>");
            }
        }
    }

#if UNITY_EDITOR
    // 디버그용: 에디터에서 Gizmos를 통해 현재 비주얼 단계 확인
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && BloodManager.HasInstance)
        {
            float ratio = BloodManager.Instance.CurrentBlood / BloodManager.Instance.MaxTargetBlood;
            int step = Mathf.Clamp(Mathf.FloorToInt(ratio * TOTAL_STEPS), 0, TOTAL_STEPS - 1);

            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f,
                $"Stage: {(step / 5) + 1} | Step: {(step % 5) + 1} ({BloodManager.Instance.CurrentBlood:F1}ml)");
        }
    }
#endif
}