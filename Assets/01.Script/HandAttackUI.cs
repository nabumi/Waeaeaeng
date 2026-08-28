using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손바닥 마스크 내부에서 중앙부터 피가 차오르는 연출을 제어하는 UI 스크립트
/// </summary>
public class HandAttackUI : MonoBehaviour
{
    [Header("UI 바인딩")]
    [SerializeField] private RectTransform fillTransform; // 마스크 내부에서 커질 Fill_Graphic의 RectTransform
    [SerializeField] private Image fillImage;             // Fill_Graphic의 Image 컴포넌트

    [Header("연출 가속 곡선")]
    [SerializeField] private AnimationCurve fillEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Coroutine chargeCoroutine;

    /// <summary>
    /// 공격 차오름 연출 시작 함수
    /// </summary>
    /// <param name="duration">차오르는 총 시간(초)</param>
    /// <param name="onComplete">차오름 완수 후 실행될 타격 판정 콜백</param>
    public void StartCharge(float duration, Action onComplete)
    {
        if (chargeCoroutine != null)
            StopCoroutine(chargeCoroutine);

        chargeCoroutine = StartCoroutine(ChargeRoutine(duration, onComplete));
    }

    private IEnumerator ChargeRoutine(float duration, Action onComplete)
    {
        float timer = 0f;

        // 초기 상태: Scale 0 (중앙점)
        if (fillTransform != null)
            fillTransform.localScale = Vector3.zero;

        while (timer < duration)
        {
            timer += Time.deltaTime;

            // 1. 선형 진행도 ($0 \to 1$)
            float linearProgress = Mathf.Clamp01(timer / duration);

            // 2. 가속 이징 적용 ($f(p) = p^2$)
            float easedProgress = fillEasing.Evaluate(linearProgress);

            // 3. [핵심] 중앙에서부터 손바닥 모양 마스크 안쪽으로 스케일 팽창!
            if (fillTransform != null)
            {
                fillTransform.localScale = new Vector3(easedProgress, easedProgress, 1f);
            }

            // 4. 타격 직전(마지막 15% 구간) 붉은색 점멸(Flicker) 연출로 위기감 조성
            if (linearProgress >= 0.85f && fillImage != null)
            {
                float flash = Mathf.Sin(Time.time * 40f) * 0.15f + 0.85f;
                fillImage.color = new Color(1f, 0f, 0f, flash);
            }

            yield return null;
        }

        // 스케일 완수 보장
        if (fillTransform != null)
            fillTransform.localScale = Vector3.one;

        // 차오름 완수 후 타격 로직 실행
        onComplete?.Invoke();
    }

    private void OnDisable()
    {
        if (chargeCoroutine != null)
            StopCoroutine(chargeCoroutine);
    }
}