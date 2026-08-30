using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen Space Canvas HUD에 고정된 위협 레이더 및 인간 분노 스택 시각화 UI 컨트롤러
/// </summary>
public class ThreatIndicatorUI : MonoBehaviour
{
    [Header("센서 참조 (모기 오브젝트의 센서 연결)")]
    [SerializeField] private MosquitoThreatSensor threatSensor;

    [Header("화면 고정 UI Image 컴포넌트 (외곽선 및 레이더)")]
    [SerializeField] private Image targetImage;

    [Header("위험도별 스프라이트 에셋")]
    [SerializeField] private Sprite safeSprite;    // 01 1.png (초록)
    [SerializeField] private Sprite warningSprite; // 02.png (노랑)
    [SerializeField] private Sprite dangerSprite;  // 03.png (빨강)

    [Header("분노 스택 게이지 (사람 형태 빨간색 차오름)")]
    [Tooltip("사람 형태 내부를 빨간색으로 채우는 Filled Image")]
    [SerializeField] private Image angerFillImage;

    [Tooltip("분노 게이지 마스크 스프라이트 (미할당 시 Resources/Sprites/ui_human_silhouette_fill 자동 로드)")]
    [SerializeField] private Sprite angerFillSprite;

    [Tooltip("기본 분노 게이지 색상 (빨강)")]
    [SerializeField] private Color angerColor = new Color(0.95f, 0.15f, 0.15f, 0.95f);

    [Tooltip("최대 분노 도달 시 강조 색상")]
    [SerializeField] private Color maxAngerColor = new Color(1.0f, 0.05f, 0.05f, 1.0f);

    [Tooltip("게이지 차오름/감소 애니메이션 속도")]
    [SerializeField] private float fillLerpSpeed = 5.0f;

    [Tooltip("스택 획득 시 펀치 팝업 효과 활성화")]
    [SerializeField] private bool enablePunchEffect = true;

    [Tooltip("최대 스택 시 강렬한 펄스(맥박) 효과 활성화")]
    [SerializeField] private bool enableMaxPulseEffect = true;

    private float currentFillRatio = 0f;
    private float targetFillRatio = 0f;
    private int lastDodgeCount = 0;
    private Coroutine punchCoroutine;
    private Vector3 originalScale = Vector3.one;

    private void Awake()
    {
        originalScale = transform.localScale;

        // 1. targetImage 미지정 시 자기 자신의 Image 컴포넌트 자동 캐싱
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        // 2. 분노 차오름용 Image 컴포넌트 및 마스크 스프라이트 무결성 보장
        EnsureAngerVisualComponents();
    }

    private void OnEnable()
    {
        // 3. 센서 위협 이벤트 구독
        if (threatSensor != null)
        {
            threatSensor.OnThreatLevelChanged += HandleThreatLevelChanged;
        }
        else
        {
            Debug.LogWarning("[ThreatIndicatorUI] threatSensor 참조가 비어있습니다! 인스펙터에서 모기를 연결해주세요.");
        }

        // 4. 인간 분노 매니저 이벤트 구독
        if (HumanAngerManager.Instance != null)
        {
            HumanAngerManager.Instance.OnAngerStackChanged += HandleAngerStackChanged;
            targetFillRatio = HumanAngerManager.Instance.AngerFillRatio;
            lastDodgeCount = HumanAngerManager.Instance.DodgeCount;
        }
    }

    private void OnDisable()
    {
        // 5. 메모리 누수 방지를 위한 이벤트 구독 해제
        if (threatSensor != null)
        {
            threatSensor.OnThreatLevelChanged -= HandleThreatLevelChanged;
        }

        if (HumanAngerManager.Instance != null)
        {
            HumanAngerManager.Instance.OnAngerStackChanged -= HandleAngerStackChanged;
        }
    }

    private void Start()
    {
        if (HumanAngerManager.Instance != null)
        {
            HumanAngerManager.Instance.OnAngerStackChanged -= HandleAngerStackChanged;
            HumanAngerManager.Instance.OnAngerStackChanged += HandleAngerStackChanged;
            targetFillRatio = HumanAngerManager.Instance.AngerFillRatio;
            lastDodgeCount = HumanAngerManager.Instance.DodgeCount;
            currentFillRatio = targetFillRatio;
            if (angerFillImage != null)
            {
                angerFillImage.fillAmount = currentFillRatio;
            }
        }
    }

    private void Update()
    {
        // 최신 분노 비율 동기화 (런타임 안정성 보장)
        if (HumanAngerManager.Instance != null)
        {
            targetFillRatio = HumanAngerManager.Instance.AngerFillRatio;
        }

        // 부드러운 게이지 차오름 연출
        currentFillRatio = Mathf.MoveTowards(currentFillRatio, targetFillRatio, Time.unscaledDeltaTime * fillLerpSpeed);

        if (angerFillImage != null)
        {
            angerFillImage.fillAmount = currentFillRatio;

            // 최대 스택 도달 시 위협적인 펄스(Glow/Blink) 연출
            if (targetFillRatio >= 1.0f && enableMaxPulseEffect)
            {
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f);
                angerFillImage.color = Color.Lerp(angerColor, maxAngerColor, pulse);
            }
            else
            {
                angerFillImage.color = angerColor;
            }
        }
    }

    /// <summary>
    /// 분노 차오름 Image 및 렌더 계층 구조를 안전하게 초기화
    /// </summary>
    private void EnsureAngerVisualComponents()
    {
        if (angerFillImage == null)
        {
            Transform existingFill = transform.Find("AngerFill");
            if (existingFill != null)
            {
                angerFillImage = existingFill.GetComponent<Image>();
            }
            else
            {
                GameObject fillObj = new GameObject("AngerFill");
                fillObj.transform.SetParent(transform, false);
                fillObj.transform.SetSiblingIndex(0); // 검정 테두리 아래에 배치하여 깔끔한 마스킹

                RectTransform rt = fillObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);

                angerFillImage = fillObj.AddComponent<Image>();
            }
        }

        // 마스크 스프라이트 자동 로드
        if (angerFillSprite == null)
        {
            angerFillSprite = Resources.Load<Sprite>("Sprites/ui_human_silhouette_fill");
        }

        if (angerFillImage != null)
        {
            if (angerFillSprite != null)
            {
                angerFillImage.sprite = angerFillSprite;
            }

            angerFillImage.type = Image.Type.Filled;
            angerFillImage.fillMethod = Image.FillMethod.Vertical;
            angerFillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            angerFillImage.fillAmount = 0f;
            angerFillImage.color = angerColor;
            angerFillImage.raycastTarget = false;
            angerFillImage.transform.SetSiblingIndex(0); // 검정 테두리보다 아래(뒤)에 렌더링
        }
    }

    /// <summary>
    /// 인간 분노 스택 변동 시 호출 (게이지 갱신 및 펀치 피드백)
    /// </summary>
    private void HandleAngerStackChanged(int currentStack, float fillRatio)
    {
        targetFillRatio = fillRatio;

        // 스택이 증가했을 때 시각적 펀치 피드백
        if (currentStack > lastDodgeCount && enablePunchEffect)
        {
            if (punchCoroutine != null)
            {
                StopCoroutine(punchCoroutine);
            }
            punchCoroutine = StartCoroutine(PunchScaleRoutine());
        }

        lastDodgeCount = currentStack;
    }

    /// <summary>
    /// 분노 스택 획득 시 센서가 살짝 커졌다가 통통 튀며 복귀하는 펀치 애니메이션
    /// </summary>
    private IEnumerator PunchScaleRoutine()
    {
        float duration = 0.25f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            // Ease-out elastic curve
            float scaleFactor = 1f + 0.15f * Mathf.Sin((1f - t) * Mathf.PI);
            transform.localScale = originalScale * scaleFactor;
            yield return null;
        }

        transform.localScale = originalScale;
        punchCoroutine = null;
    }

    /// <summary>
    /// 모기의 위협 수준이 변할 때만 신호를 받아 고정 UI 스프라이트를 교체
    /// </summary>
    private void HandleThreatLevelChanged(ThreatLevel level, float dangerRatio)
    {
        if (targetImage == null) return;

        // C# 8.0 switch 표현식을 통한 스프라이트 분기
        Sprite selectedSprite = level switch
        {
            ThreatLevel.Safe => safeSprite,
            ThreatLevel.Warning => warningSprite,
            ThreatLevel.Danger => dangerSprite,
            _ => safeSprite
        };

        // UI Image 에셋 교체
        targetImage.sprite = selectedSprite;

        Debug.Log($"<color=cyan>[HUD 레이더] 상태 변경 -> {level} ({dangerRatio * 100f:F0}%)</color>");
    }
}