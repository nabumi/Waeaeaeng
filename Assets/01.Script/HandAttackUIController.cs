using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손바닥 모양 그림자 커짐 연출, 손바닥 강타(Slam), HIT 이펙트 연출을
/// 순차적으로 시퀀싱하여 처리하는 타격 UI 제어 클래스.
/// </summary>
public class HandAttackUIController : MonoBehaviour
{
    [Header("UI 바인딩 리소스")]
    [Tooltip("손바닥 모양 검은색/어두운 실루엣 그림자 RectTransform")]
    [SerializeField] private RectTransform shadowRectTransform;

    [Tooltip("손바닥 그림자 Image (알파값 조절용)")]
    [SerializeField] private Image shadowImage;

    [Tooltip("실제 내려치는 손바닥 그래픽 GameObject")]
    [SerializeField] private GameObject handGraphicObject;

    [Tooltip("손바닥 타격 시 출력될 HIT 이펙트 UI GameObject")]
    [SerializeField] private GameObject hitEffectObject;

    [Header("연출 세부 설정")]
    [Tooltip("그림자 시작 스케일 (0.2 = 20% 크기부터 시작)")]
    [SerializeField] private float minShadowScale = 0.2f;

    // [수정] AnimationCurve.EaseIn -> AnimationCurve.EaseInOut 올바른 API 사용
    [Tooltip("차오름 속도 가속 커브 (Ease-In-Out 계열)")]
    [SerializeField] private AnimationCurve fillEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("손바닥이 내리치는 Slam 연출 시간 (초)")]
    [SerializeField] private float handSlamDuration = 0.08f;

    [Tooltip("HIT 이펙트 유지 시간 (초)")]
    [SerializeField] private float hitEffectDuration = 0.35f;

    // 위치 고정 및 상태 변수
    private Vector2 targetWorldPosition;
    private Canvas parentCanvas;
    private Camera mainCamera;
    private bool isInitialized = false;
    private Coroutine attackSequenceCoroutine;

    /// <summary>
    /// UI 생성 직후 위치 및 회전을 설정합니다.
    /// </summary>
    public void Initialize(Vector2 worldPos, Canvas canvas, float zRotationAngle = 0f)
    {
        this.targetWorldPosition = worldPos;
        this.parentCanvas = canvas;
        this.mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

        // Z축 랜덤 회전 적용
        RectTransform rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localRotation = Quaternion.Euler(0f, 0f, zRotationAngle);
        }

        // 초기 오브젝트 상태 정리
        if (handGraphicObject != null) handGraphicObject.SetActive(false);
        if (hitEffectObject != null) hitEffectObject.SetActive(false);

        UpdateUIPosition();
        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized) return;
        UpdateUIPosition();
    }

    /// <summary>
    /// 그림자 확대 -> 손바닥 강타 및 데미지 판정 -> HIT 이펙트 연출 시퀀스를 실행합니다.
    /// </summary>
    public void StartHandAttackSequence(float chargeDuration, Action onHitImpact, Action onSequenceComplete)
    {
        if (attackSequenceCoroutine != null)
            StopCoroutine(attackSequenceCoroutine);

        attackSequenceCoroutine = StartCoroutine(HandAttackSequenceRoutine(chargeDuration, onHitImpact, onSequenceComplete));
    }

    private IEnumerator HandAttackSequenceRoutine(float chargeDuration, Action onHitImpact, Action onSequenceComplete)
    {
        float timer = 0f;

        // ------------------------------------------------------------------
        // Phase 1: 그림자 차오름 연출 (Shadow Scaling Phase)
        // ------------------------------------------------------------------
        if (shadowRectTransform != null)
        {
            shadowRectTransform.localScale = Vector3.one * minShadowScale;
        }

        while (timer < chargeDuration)
        {
            timer += Time.deltaTime;

            // 선형 진행율: $p = \min\left(1, \frac{t}{T}\right)$
            float linearProgress = Mathf.Clamp01(timer / chargeDuration);
            float easedProgress = fillEasing.Evaluate(linearProgress);

            // 스케일 보간 공식: $S(t) = S_{\min} + (1 - S_{\min}) \cdot f(t)$
            float currentScale = Mathf.Lerp(minShadowScale, 1.0f, easedProgress);

            if (shadowRectTransform != null)
            {
                shadowRectTransform.localScale = new Vector3(currentScale, currentScale, 1f);
            }

            // 그림자 투명도(Alpha) 보간
            if (shadowImage != null)
            {
                Color color = shadowImage.color;
                color.a = Mathf.Lerp(0.3f, 0.85f, easedProgress);
                shadowImage.color = color;
            }

            yield return null;
        }

        // ------------------------------------------------------------------
        // Phase 2: 손바닥 강타 연출 및 HIT 판정 (Slam & Hit Impact Phase)
        // ------------------------------------------------------------------
        if (handGraphicObject != null)
        {
            handGraphicObject.SetActive(true);
            RectTransform handRect = handGraphicObject.GetComponent<RectTransform>();

            float slamTimer = 0f;
            while (slamTimer < handSlamDuration)
            {
                slamTimer += Time.deltaTime;
                float slamProgress = Mathf.Clamp01(slamTimer / handSlamDuration);

                if (handRect != null)
                {
                    // 손바닥이 $1.4 \to 1.0$ 스케일로 압축되며 착지
                    float slamScale = Mathf.Lerp(1.4f, 1.0f, slamProgress);
                    handRect.localScale = new Vector3(slamScale, slamScale, 1f);
                }
                yield return null;
            }
        }

        // 손바닥 타격 피크 프레임에서 히트 판정 콜백 호출 및 타격음 재생
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Slap);
        onHitImpact?.Invoke();

        // ------------------------------------------------------------------
        // Phase 3: HIT 이펙트 연출 (Hit Visual Effect Phase)
        // ------------------------------------------------------------------
        if (hitEffectObject != null)
        {
            hitEffectObject.SetActive(true);
        }

        if (shadowRectTransform != null)
        {
            shadowRectTransform.gameObject.SetActive(false);
        }

        yield return new WaitForSeconds(hitEffectDuration);

        // 연출 완료 및 파괴 콜백 호출
        onSequenceComplete?.Invoke();
    }

    private void UpdateUIPosition()
    {
        if (parentCanvas == null || mainCamera == null) return;

        RectTransform rectTransform = GetComponent<RectTransform>();

        if (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            rectTransform.position = mainCamera.WorldToScreenPoint(targetWorldPosition);
        }
        else if (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            Camera renderCam = parentCanvas.worldCamera != null ? parentCanvas.worldCamera : mainCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform,
                mainCamera.WorldToScreenPoint(targetWorldPosition),
                renderCam,
                out Vector2 localPoint
            );
            rectTransform.anchoredPosition = localPoint;
        }
        else
        {
            rectTransform.position = targetWorldPosition;
        }
    }

    private void OnDisable()
    {
        if (attackSequenceCoroutine != null)
        {
            StopCoroutine(attackSequenceCoroutine);
            attackSequenceCoroutine = null;
        }
    }
}