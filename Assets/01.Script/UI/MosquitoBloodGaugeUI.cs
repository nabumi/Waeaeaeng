using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 모기 머리 위/주변에 부착되어 현재 잔여 혈액량 및 소모 상태를 상시 표시하는 월드 스페이스 게이지 UI
/// </summary>
public class MosquitoBloodGaugeUI : MonoBehaviour
{
    [Header("추적 대상")]
    [SerializeField] private Transform targetMosquito;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.75f, 0f);

    [Header("게이지 바 컴포넌트")]
    [SerializeField] private Image fillImage;
    [SerializeField] private Image bgImage;
    [SerializeField] private TextMeshProUGUI bloodText;

    [Header("게이지 색상 그라디언트")]
    [SerializeField] private Color fullColor = new Color(0.2f, 0.85f, 0.3f, 0.95f);    // 안전 (초록)
    [SerializeField] private Color warningColor = new Color(0.95f, 0.75f, 0.1f, 0.95f); // 주의 (노랑)
    [SerializeField] private Color criticalColor = new Color(0.95f, 0.15f, 0.15f, 0.95f); // 위험/고갈 (빨강)

    private Camera mainCamera;
    private Canvas canvas;

    private void Awake()
    {
        mainCamera = Camera.main;
        canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
        }

        // 컴포넌트 각각을 독립적으로 체크하여 누락 방지
        EnsureVisualComponents();
    }

    private void OnEnable()
    {
        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.OnBloodAmountChanged += UpdateGauge;
            UpdateGauge(BloodManager.Instance.CurrentBlood, BloodManager.Instance.MaxTargetBlood);
        }
    }

    private void OnDisable()
    {
        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.OnBloodAmountChanged -= UpdateGauge;
        }
    }

    private void Start()
    {
        FindTargetMosquito();

        if (BloodManager.Instance != null)
        {
            UpdateGauge(BloodManager.Instance.CurrentBlood, BloodManager.Instance.MaxTargetBlood);
        }
    }

    private void LateUpdate()
    {
        if (targetMosquito == null)
        {
            FindTargetMosquito();
            if (targetMosquito == null) return;
        }

        // 모기 위치에 오프셋을 더해 따라다님
        transform.position = targetMosquito.position + offset;

        // 빌보드 처리: 카메라 방향을 바라보도록 회전 고정
        if (mainCamera != null)
        {
            transform.rotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// 모기 트랜스폼 탐색 (LateUpdate 내 무분별한 FindAnyObjectByType 방지)
    /// </summary>
    private void FindTargetMosquito()
    {
        if (targetMosquito != null) return;

        var mosquito = FindAnyObjectByType<MosquitoController>();
        if (mosquito != null)
        {
            targetMosquito = mosquito.transform;
        }
    }

    /// <summary>
    /// 혈액량 변동 시 게이지 Fill 및 색상/텍스트 갱신 (Zero-GC 최적화)
    /// </summary>
    public void UpdateGauge(float current, float max)
    {
        // 백분율 $ratio = \text{Clamp01}(current / max)$
        float ratio = Mathf.Clamp01(current / max);

        // 1. 게이지 바 Fill 및 색상 보간
        if (fillImage != null)
        {
            fillImage.fillAmount = ratio;

            // 색상 보간: $ratio > 0.5$ 일 때 초록->노랑, 이하일 때 노랑->빨강
            if (ratio > 0.5f)
            {
                float t = (ratio - 0.5f) / 0.5f;
                fillImage.color = Color.Lerp(warningColor, fullColor, t);
            }
            else
            {
                float t = ratio / 0.5f;
                fillImage.color = Color.Lerp(criticalColor, warningColor, t);
            }
        }

        // 2. 텍스트 갱신 (TMP 전용 SetText 사용으로 GC Alloc $0\text{ bytes}$ 달성)
        if (bloodText != null)
        {
            int currentInt = Mathf.CeilToInt(current);
            bloodText.SetText("{0} ml", currentInt);
            bloodText.color = Color.white; // [요청 반영] 피가 부족해도 항상 흰색으로 선명하게 유지
        }
    }

    /// <summary>
    /// 인스펙터 바인딩이 누락되었을 때 개별 컴포넌트 단위로 안전하게 런타임 자동 구성
    /// </summary>
    private void EnsureVisualComponents()
    {
        // Canvas 설정
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100;
        }

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta = new Vector2(1.2f, 0.22f);
            rt.localScale = Vector3.one;
        }

        // 1. 배경 바 독립 검사
        if (bgImage == null)
        {
            Transform bgTransform = transform.Find("Gauge_BG");
            if (bgTransform != null)
            {
                bgImage = bgTransform.GetComponent<Image>();
            }
            else
            {
                var bgObj = new GameObject("Gauge_BG");
                bgObj.transform.SetParent(transform, false);
                var bgRt = bgObj.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.sizeDelta = Vector2.zero;

                bgImage = bgObj.AddComponent<Image>();
                bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);
            }
        }

        // 2. 채움 바 독립 검사
        if (fillImage == null)
        {
            Transform fillTransform = transform.Find("Gauge_Fill");
            if (fillTransform != null)
            {
                fillImage = fillTransform.GetComponent<Image>();
            }
            else
            {
                var fillObj = new GameObject("Gauge_Fill");
                fillObj.transform.SetParent(transform, false);
                var fillRt = fillObj.AddComponent<RectTransform>();
                fillRt.anchorMin = new Vector2(0.04f, 0.15f);
                fillRt.anchorMax = new Vector2(0.96f, 0.85f);
                fillRt.sizeDelta = Vector2.zero;

                fillImage = fillObj.AddComponent<Image>();
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = 0;
                fillImage.color = fullColor;
            }
        }

        // 3. 수치 텍스트 독립 검사 (성급한 리턴 제거로 100% 실행 보장)
        if (bloodText == null)
        {
            Transform textTransform = transform.Find("Gauge_Text");
            if (textTransform != null)
            {
                bloodText = textTransform.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                var textObj = new GameObject("Gauge_Text");
                textObj.transform.SetParent(transform, false);
                var textRt = textObj.AddComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.sizeDelta = Vector2.zero;

                bloodText = textObj.AddComponent<TextMeshProUGUI>();
                bloodText.alignment = TextAlignmentOptions.Center;
                bloodText.fontSize = 0.35f;
                bloodText.text = "40 ml";
                bloodText.color = Color.white;
            }
        }
    }
}