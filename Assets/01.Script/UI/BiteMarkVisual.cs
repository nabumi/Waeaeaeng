// BiteMarkVisual.cs - PF_BiteMark에 부착할 연출 스크립트
using System.Collections;
using UnityEngine;

public class BiteMarkVisual : MonoBehaviour
{
    [Header("Spawn Animation Settings")]
    [SerializeField] private float popDuration = 0.2f; // 스케일 커지는 시간
    [SerializeField] private Vector3 targetScale = Vector3.one; // 최종 크기

    private void OnEnable()
    {
        // 생성되는 순간 뿅! 하고 커지는 코루틴 연출 실행
        StartCoroutine(AnimateSpawnRoutine());
    }

    private IEnumerator AnimateSpawnRoutine()
    {
        transform.localScale = Vector3.zero; // 처음엔 크기 0
        float elapsedTime = 0f;

        while (elapsedTime < popDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / popDuration;

            // Easing 효과로 부드럽게 크기 확대
            transform.localScale = Vector3.Lerp(Vector3.zero, targetScale, progress);
            yield return null;
        }

        transform.localScale = targetScale;
    }
}