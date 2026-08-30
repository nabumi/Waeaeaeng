using UnityEngine;

/// <summary>
/// 모기 아래에 자연스러운 2.5D 타원형 그림자를 생성하고, 호버링 높낮이에 따라 크기와 투명도를 동적으로 제어하는 컴포넌트
/// </summary>
public class MosquitoShadowController : MonoBehaviour
{
    [Header("그림자 위치 및 오프셋")]
    [Tooltip("모기 본체 기준 비행 중 그림자의 고정 Y축 아래 오프셋")]
    [SerializeField] private float baseGroundOffsetY = -0.50f;
    [Tooltip("착륙 시 모기 본체 기준 그림자 Y축 오프셋 (잘 보이도록 아래로 배치)")]
    [SerializeField] private float landingGroundOffsetY = -0.38f;
    [Tooltip("모기 본체 기준 그림자의 X축 오프셋 (빛 방향에 따른 미세 조절)")]
    [SerializeField] private float shadowOffsetX = 0.05f;

    [Header("그림자 크기 및 비율")]
    [Tooltip("그림자 기본 가로 크기")]
    [SerializeField] private float shadowWidth = 0.90f;
    [Tooltip("그림자 기본 세로(납작한 타원) 크기")]
    [SerializeField] private float shadowHeight = 0.32f;

    [Header("투명도 설정")]
    [Tooltip("기본 그림자 투명도 (0.0 ~ 1.0)")]
    [Range(0f, 1f)]
    [SerializeField] private float baseShadowAlpha = 0.50f;

    private Transform shadowTransform;
    private SpriteRenderer shadowRenderer;
    private MosquitoController mosquitoController;
    private static Sprite proceduralShadowSprite;

    private void Awake()
    {
        mosquitoController = GetComponentInParent<MosquitoController>() ?? GetComponent<MosquitoController>();
        CreateShadowObject();
    }

    private void CreateShadowObject()
    {
        // 1. 이미 자식 그림자가 있는지 확인
        Transform existing = transform.Find("MosquitoShadow");
        if (existing != null)
        {
            shadowTransform = existing;
            shadowRenderer = existing.GetComponent<SpriteRenderer>();
        }
        else
        {
            GameObject shadowObj = new GameObject("MosquitoShadow");
            shadowObj.transform.SetParent(transform, false);
            shadowTransform = shadowObj.transform;
            shadowRenderer = shadowObj.AddComponent<SpriteRenderer>();
        }

        // 2. 부드러운 안티앨리어싱 타원형 그림자 스프라이트 생성/할당
        if (proceduralShadowSprite == null)
        {
            proceduralShadowSprite = GenerateSoftCircleSprite();
        }

        if (shadowRenderer != null)
        {
            shadowRenderer.sprite = proceduralShadowSprite;
            shadowRenderer.sortingOrder = 0; // 모기 본체(10), 꼬리(2)보다 아래 레이어
            shadowRenderer.color = new Color(0f, 0f, 0f, baseShadowAlpha);
        }

        UpdateShadowTransform(false);
    }

    private void LateUpdate()
    {
        if (shadowTransform == null || shadowRenderer == null) return;

        // 1. 모기 사망 시 그림자 숨김
        if (mosquitoController != null && mosquitoController.IsDead)
        {
            shadowRenderer.enabled = false;
            return;
        }

        // 2. 모기 상태에 따른 그림자 위치 및 가시성 연산
        MosquitoState state = mosquitoController != null ? mosquitoController.CurrentState : MosquitoState.Flying;
        bool isLanded = (state == MosquitoState.Landing || state == MosquitoState.Checking || state == MosquitoState.Sucking);

        shadowRenderer.enabled = true;
        UpdateShadowTransform(isLanded);
    }

    private void UpdateShadowTransform(bool isLanded)
    {
        if (shadowTransform == null || shadowRenderer == null) return;

        // 꼬리 및 바라보는 방향에 맞춰 X 오프셋 반전
        bool isFacingRight = mosquitoController == null || mosquitoController.IsFacingRight;
        float currentOffsetX = isFacingRight ? -shadowOffsetX : shadowOffsetX;

        if (isLanded)
        {
            // [요청 반영] 착륙 시 모기 아래 피부 표면에 잘 보이도록 아래(-0.38f)로 배치
            shadowTransform.localPosition = new Vector3(currentOffsetX, landingGroundOffsetY, 0f);
            shadowTransform.localScale = new Vector3(shadowWidth * 0.95f, shadowHeight * 0.95f, 1f);
            shadowRenderer.color = new Color(0f, 0f, 0f, Mathf.Min(1.0f, baseShadowAlpha * 1.15f));
        }
        else
        {
            // [요청 반영] 모기 본체가 이미 호버링으로 움직이므로, 그림자는 흔들림 없이 고정된 상대 위치에 안정적으로 유지
            shadowTransform.localPosition = new Vector3(currentOffsetX, baseGroundOffsetY, 0f);
            shadowTransform.localScale = new Vector3(shadowWidth, shadowHeight, 1f);
            shadowRenderer.color = new Color(0f, 0f, 0f, baseShadowAlpha);
        }
    }

    /// <summary>
    /// 외부 텍스처 에셋 없이도 완벽하게 부드러운 안티앨리어싱 타원 그림자를 런타임 생성
    /// </summary>
    private static Sprite GenerateSoftCircleSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) / 2f;
        float radius = center - 2f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float normalizedDist = dist / radius;

                // 부드러운 깃털(Feather) 감쇄 곡선
                float alpha = Mathf.Clamp01(1f - normalizedDist);
                alpha = Mathf.SmoothStep(0f, 1f, alpha);
                alpha = Mathf.Pow(alpha, 1.5f);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private void OnDisable()
    {
        if (shadowRenderer != null)
        {
            shadowRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (shadowRenderer != null)
        {
            shadowRenderer.enabled = true;
        }
    }
}