using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen Space Canvas HUD에 고정된 위협 레이더 UI 컨트롤러
/// </summary>
public class ThreatIndicatorUI : MonoBehaviour
{
    [Header("센서 참조 (모기 오브젝트의 센서 연결)")]
    [SerializeField] private MosquitoThreatSensor threatSensor;

    [Header("화면 고정 UI Image 컴포넌트")]
    [SerializeField] private Image targetImage;

    [Header("위험도별 스프라이트 에셋")]
    [SerializeField] private Sprite safeSprite;    // 01.png (초록)
    [SerializeField] private Sprite warningSprite; // 02.png (노랑)
    [SerializeField] private Sprite dangerSprite;  // 03.png (빨강)

    private void Awake()
    {
        // 1. targetImage 미지정 시 자기 자신의 Image 컴포넌트 자동 캐싱
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }
    }

    private void OnEnable()
    {
        // 2. 센서 이벤트 구독 (옵저버 패턴)
        if (threatSensor != null)
        {
            threatSensor.OnThreatLevelChanged += HandleThreatLevelChanged;
        }
        else
        {
            Debug.LogWarning("[ThreatIndicatorUI] threatSensor 참조가 비어있습니다! 인스펙터에서 모기를 연결해주세요.");
        }
    }

    private void OnDisable()
    {
        // 3. 메모리 누수 방지를 위한 이벤트 구독 해제
        if (threatSensor != null)
        {
            threatSensor.OnThreatLevelChanged -= HandleThreatLevelChanged;
        }
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