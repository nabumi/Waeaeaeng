using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 만복(150ml 이상) 시:
/// 1. 화면 중앙 상단에 탈출구 남은 거리 표시 (나눔손글씨 폰트 적용)
/// 2. 화면 외곽 테두리를 돌며 탈출구 방향을 가리키는 화살표 표시
/// </summary>
public class EscapeIndicatorUI : MonoBehaviour
{
    private static EscapeIndicatorUI instance;
    public static EscapeIndicatorUI Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<EscapeIndicatorUI>();
                if (instance == null)
                {
                    var canvasGo = new GameObject("[EscapeIndicatorCanvas]");
                    var canvas = canvasGo.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 999;
                    var scaler = canvasGo.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;
                    canvasGo.AddComponent<GraphicRaycaster>();

                    var indicatorGo = new GameObject("EscapeIndicator");
                    indicatorGo.transform.SetParent(canvasGo.transform, false);
                    instance = indicatorGo.AddComponent<EscapeIndicatorUI>();
                }
            }
            return instance;
        }
    }

    [Header("화면 외곽 마진 설정 (px)")]
    [SerializeField] private float edgeMargin = 70f;

    [Header("폰트 에셋")]
    [SerializeField] private TMP_FontAsset koreanFont;

    private RectTransform arrowRect;
    private Image arrowImage;

    private RectTransform topBannerRect;
    private TextMeshProUGUI topDistanceText;
    private Canvas parentCanvas;

    private Vector2 targetWorldPosition;
    private bool isTracking = false;
    private Camera mainCamera;
    private Transform playerTransform;

    private void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        parentCanvas = GetComponentInParent<Canvas>();
        mainCamera = Camera.main;

        LoadKoreanFont();
        EnsureVisualComponents();
        HideIndicator();
    }

    private void LoadKoreanFont()
    {
        if (koreanFont == null)
        {
            koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NanumPen SDF");
        }
    }

    private void Start()
    {
        FindPlayer();
    }

    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        else
        {
            var mosquito = FindAnyObjectByType<MosquitoController>();
            if (mosquito != null) playerTransform = mosquito.transform;
        }
    }

    private void LateUpdate()
    {
        if (!isTracking) return;

        if (mainCamera == null)
        {
            mainCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            if (mainCamera == null) return;
        }

        if (playerTransform == null) FindPlayer();

        UpdateArrowPositionAndRotation();
        UpdateTopDistanceText();
    }

    public void ShowIndicator(Vector2 escapeWorldPos)
    {
        targetWorldPosition = escapeWorldPos;
        isTracking = true;

        LoadKoreanFont();
        EnsureVisualComponents();
        gameObject.SetActive(true);

        if (arrowImage != null) arrowImage.enabled = true;
        if (topBannerRect != null) topBannerRect.gameObject.SetActive(true);
        if (topDistanceText != null) topDistanceText.enabled = true;

        Debug.LogWarning($"<color=green>[EscapeIndicatorUI] 탈출 방향 및 상단 중앙 거리 UI 활성화 -> 목표 위치: {escapeWorldPos}</color>");
    }

    public void HideIndicator()
    {
        isTracking = false;
        if (topBannerRect != null) topBannerRect.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    private void UpdateArrowPositionAndRotation()
    {
        if (arrowRect == null || mainCamera == null) return;

        Vector3 screenPos = mainCamera.WorldToScreenPoint(targetWorldPosition);
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (screenPos.z < 0)
        {
            screenPos = -screenPos;
        }

        Vector2 dirFromCenter = ((Vector2)screenPos - screenCenter).normalized;
        if (dirFromCenter == Vector2.zero) dirFromCenter = Vector2.up;

        float angle = Mathf.Atan2(dirFromCenter.y, dirFromCenter.x) * Mathf.Rad2Deg;
        arrowRect.rotation = Quaternion.Euler(0, 0, angle - 90f);

        float minX = edgeMargin;
        float maxX = Screen.width - edgeMargin;
        float minY = edgeMargin;
        float maxY = Screen.height - edgeMargin;

        Vector2 clampedPos = screenPos;
        bool isOffScreen = screenPos.x <= minX || screenPos.x >= maxX || screenPos.y <= minY || screenPos.y >= maxY || screenPos.z < 0;

        if (isOffScreen)
        {
            float slope = dirFromCenter.y / (dirFromCenter.x == 0 ? 0.0001f : dirFromCenter.x);

            if (dirFromCenter.x > 0)
            {
                clampedPos.x = maxX;
                clampedPos.y = screenCenter.y + slope * (maxX - screenCenter.x);
            }
            else
            {
                clampedPos.x = minX;
                clampedPos.y = screenCenter.y + slope * (minX - screenCenter.x);
            }

            if (clampedPos.y > maxY)
            {
                clampedPos.y = maxY;
                clampedPos.x = screenCenter.x + (maxY - screenCenter.y) / slope;
            }
            else if (clampedPos.y < minY)
            {
                clampedPos.y = minY;
                clampedPos.x = screenCenter.x + (minY - screenCenter.y) / slope;
            }
        }

        arrowRect.position = clampedPos;
    }

    private void UpdateTopDistanceText()
    {
        if (topDistanceText == null || playerTransform == null) return;

        float dist = Vector2.Distance(playerTransform.position, targetWorldPosition);
        topDistanceText.text = $"탈출구까지: {dist:F1} m";
    }

    private void EnsureVisualComponents()
    {
        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();

        // 1. 화면 외곽 화살표
        var arrowObj = transform.Find("ArrowIcon");
        if (arrowObj == null)
        {
            var go = new GameObject("ArrowIcon");
            go.transform.SetParent(transform, false);
            arrowRect = go.AddComponent<RectTransform>();
            arrowImage = go.AddComponent<Image>();
        }
        else
        {
            arrowRect = arrowObj.GetComponent<RectTransform>();
            arrowImage = arrowObj.GetComponent<Image>();
        }

        if (arrowRect != null)
        {
            arrowRect.sizeDelta = new Vector2(80, 80);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
        }

        if (arrowImage != null)
        {
            var arrowSprites = Resources.LoadAll<Sprite>("Sprites/화살표");
            if (arrowSprites != null && arrowSprites.Length > 0)
            {
                arrowImage.sprite = arrowSprites[0];
            }
            arrowImage.color = new Color(0.1f, 1.0f, 0.4f, 0.95f);
            arrowImage.raycastTarget = false;
        }

        // 2. 화면 중앙 상단 거리 배너 패널
        var bannerObj = transform.Find("TopDistanceBanner");
        if (bannerObj == null)
        {
            var go = new GameObject("TopDistanceBanner");
            go.transform.SetParent(transform, false);
            topBannerRect = go.AddComponent<RectTransform>();

            // 화면 상단 중앙 앵커
            topBannerRect.anchorMin = new Vector2(0.5f, 1.0f);
            topBannerRect.anchorMax = new Vector2(0.5f, 1.0f);
            topBannerRect.pivot = new Vector2(0.5f, 1.0f);
            topBannerRect.anchoredPosition = new Vector2(0, -30);
            topBannerRect.sizeDelta = new Vector2(340, 60);

            // 반투명 배경 박스
            var bgImage = go.AddComponent<Image>();
            bgImage.color = new Color(0.1f, 0.15f, 0.1f, 0.8f);
            bgImage.raycastTarget = false;

            // 텍스트 오브젝트
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(go.transform, false);
            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            topDistanceText = textObj.AddComponent<TextMeshProUGUI>();
            topDistanceText.alignment = TextAlignmentOptions.Center;
            topDistanceText.fontSize = 38;
            topDistanceText.color = new Color(0.3f, 1.0f, 0.5f, 1.0f);
            topDistanceText.raycastTarget = false;
        }
        else
        {
            topBannerRect = bannerObj.GetComponent<RectTransform>();
            topDistanceText = bannerObj.GetComponentInChildren<TextMeshProUGUI>();
        }

        // 나눔손글씨 폰트 적용
        if (topDistanceText != null && koreanFont != null)
        {
            topDistanceText.font = koreanFont;
        }
    }
}
