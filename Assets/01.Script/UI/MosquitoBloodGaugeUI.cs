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

        // 자체 구성이 안 되어 있을 경우 기본 비주얼 자동 생성
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
        if (targetMosquito == null)
        {
            var mosquito = FindAnyObjectByType<MosquitoController>();
            if (mosquito != null) targetMosquito = mosquito.transform;
        }

        if (BloodManager.Instance != null)
        {
            UpdateGauge(BloodManager.Instance.CurrentBlood, BloodManager.Instance.MaxTargetBlood);
        }
    }

    private void LateUpdate()
    {
        if (targetMosquito == null)
        {
            var mosquito = FindAnyObjectByType<MosquitoController>();
            if (mosquito != null) targetMosquito = mosquito.transform;
            else return;
        }

        // 모기 위치에 오프셋을 더해 따라다님
        transform.position = targetMosquito.position + offset;

        // 카메라 방향을 바라보도록 회전 고정
        if (mainCamera != null)
        {
            transform.rotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// 혈액량 변동 시 게이지 Fill 및 색상/텍스트 갱신
    /// </summary>
    public void UpdateGauge(float current, float max)
    {
        float ratio = Mathf.Clamp01(current / max);

        if (fillImage != null)
        {
            fillImage.fillAmount = ratio;

            // 색상 보간: 0.5 이상 초록->노랑, 0.5 미만 노랑->빨강
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

        if (bloodText != null)
        {
            bloodText.text = $"{Mathf.CeilToInt(current)} ml";
            bloodText.color = Color.white;
        }
    }

    /// <summary>
    /// 인스펙터 바인딩이 누락되었을 때 코드로 깔끔한 WorldSpace 게이지 구성
    /// </summary>
    private void EnsureVisualComponents()
    {
        if (fillImage != null && bgImage != null) return;

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

        // 배경 바
        if (bgImage == null)
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

        // 채움 바
        if (fillImage == null)
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

        // 수치 텍스트
        if (bloodText == null)
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
