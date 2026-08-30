using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// [URP 규격] 불릿타임(슬로우모션/대시) 발동 시 화면에 비네트, 색수차, 렌즈 왜곡 등 영화 같은 시네마틱 후처리를 연출하는 컨트롤러
/// </summary>
public class BulletTimePostProcessController : MonoBehaviour
{
    private static BulletTimePostProcessController instance;
    public static BulletTimePostProcessController Instance
    {
        get
        {
            if (instance == null)
            {
                var existing = FindAnyObjectByType<BulletTimePostProcessController>();
                if (existing != null)
                {
                    instance = existing;
                }
                else
                {
                    GameObject go = new GameObject("[BulletTimePostProcessController]");
                    instance = go.AddComponent<BulletTimePostProcessController>();
                }
            }
            return instance;
        }
    }

    [Header("후처리 볼륨")]
    [SerializeField] private Volume postProcessVolume;

    [Header("후처리 효과 설정")]
    [SerializeField] private float maxVignette = 0.20f; // [요청 반영] 어두워지는 효과 절반으로 감소
    [SerializeField] private float maxChromaticAberration = 0.65f;
    [SerializeField] private float maxLensDistortion = -0.15f;
    [SerializeField] private float maxSaturationDebuff = -5f;

    private Vignette vignette;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private ColorAdjustments colorAdjustments;

    private Coroutine activeTransitionCoroutine;

    private void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        EnsurePostProcessVolume();
    }

    /// <summary>
    /// URP Global Volume 및 오버라이드 효과 초기화 (Zero-GC)
    /// </summary>
    private void EnsurePostProcessVolume()
    {
        if (postProcessVolume == null)
        {
            postProcessVolume = GetComponent<Volume>();
            if (postProcessVolume == null)
            {
                postProcessVolume = gameObject.AddComponent<Volume>();
            }
        }

        postProcessVolume.isGlobal = true;
        postProcessVolume.priority = 100; // 최우선 렌더링
        postProcessVolume.weight = 0f;    // 기본 평상시 0

        // Profile 구성
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "BulletTimeVolumeProfile";

        // 1. 비네트 (Vignette)
        if (!profile.TryGet(out vignette))
        {
            vignette = profile.Add<Vignette>(true);
        }
        vignette.intensity.overrideState = true;
        vignette.intensity.value = maxVignette;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.70f; // 더욱 부드럽고 자연스러운 그라데이션
        vignette.color.overrideState = true;
        vignette.color.value = new Color(0.05f, 0.05f, 0.05f, 1f);

        // 2. 색수차 (Chromatic Aberration)
        if (!profile.TryGet(out chromaticAberration))
        {
            chromaticAberration = profile.Add<ChromaticAberration>(true);
        }
        chromaticAberration.intensity.overrideState = true;
        chromaticAberration.intensity.value = maxChromaticAberration;

        // 3. 렌즈 왜곡 (Lens Distortion)
        if (!profile.TryGet(out lensDistortion))
        {
            lensDistortion = profile.Add<LensDistortion>(true);
        }
        lensDistortion.intensity.overrideState = true;
        lensDistortion.intensity.value = maxLensDistortion;
        lensDistortion.scale.overrideState = true;
        lensDistortion.scale.value = 1.0f;

        // 4. 색조/대비 (Color Adjustments)
        if (!profile.TryGet(out colorAdjustments))
        {
            colorAdjustments = profile.Add<ColorAdjustments>(true);
        }
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.saturation.value = maxSaturationDebuff;
        colorAdjustments.contrast.overrideState = true;
        colorAdjustments.contrast.value = 5f;

        postProcessVolume.profile = profile;
    }

    /// <summary>
    /// 대시/불릿타임 발동 시 호출되어 지정된 시간 동안 후처리를 부드럽게 켜고 끕니다.
    /// </summary>
    /// <param name="duration">대시 지속 시간 (초)</param>
    public void TriggerBulletTimeEffect(float duration)
    {
        if (postProcessVolume == null) EnsurePostProcessVolume();

        if (activeTransitionCoroutine != null)
        {
            StopCoroutine(activeTransitionCoroutine);
        }
        activeTransitionCoroutine = StartCoroutine(BulletTimeRoutine(duration));
    }

    private IEnumerator BulletTimeRoutine(float duration)
    {
        // 1. 빠른 인입 (0.04초 만에 Weight 0.0 -> 1.0)
        float inDuration = 0.04f;
        float timer = 0f;
        float startWeight = postProcessVolume.weight;

        while (timer < inDuration)
        {
            timer += Time.unscaledDeltaTime;
            postProcessVolume.weight = Mathf.Lerp(startWeight, 1.0f, timer / inDuration);
            yield return null;
        }
        postProcessVolume.weight = 1.0f;

        // 2. 불릿타임 지속 시간 대기
        float holdDuration = Mathf.Max(0f, duration - inDuration - 0.12f);
        if (holdDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(holdDuration);
        }

        // 3. 부드러운 페이드아웃 복귀 (0.12초 동안 Weight 1.0 -> 0.0)
        float outDuration = 0.12f;
        timer = 0f;

        while (timer < outDuration)
        {
            timer += Time.unscaledDeltaTime;
            postProcessVolume.weight = Mathf.Lerp(1.0f, 0.0f, timer / outDuration);
            yield return null;
        }

        postProcessVolume.weight = 0.0f;
        activeTransitionCoroutine = null;
    }

    /// <summary>
    /// 사망/씬 전환/일시정지 등 즉각 리셋이 필요할 때 호출
    /// </summary>
    public void ResetEffectImmediate()
    {
        if (activeTransitionCoroutine != null)
        {
            StopCoroutine(activeTransitionCoroutine);
            activeTransitionCoroutine = null;
        }
        if (postProcessVolume != null)
        {
            postProcessVolume.weight = 0.0f;
        }
    }

    private void OnDestroy()
    {
        ResetEffectImmediate();
    }
}