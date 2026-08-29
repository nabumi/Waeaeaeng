using TMPro;
using UnityEngine;

/// <summary>
/// 인게임 HUD의 타이머 텍스트(예: 02:37)를 실시간으로 갱신하며,
/// 게임 종료(사망 또는 클리어) 시 스스로 비활성화되어 화면을 정돈하는 자율형 UI 컴포넌트
/// </summary>
public class InGameTimerUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timerText;

    private void Awake()
    {
        if (timerText == null)
        {
            timerText = GetComponent<TextMeshProUGUI>();
        }
    }

    private void OnEnable()
    {
        // 1. 실시간 타이머 갱신 이벤트 구독
        PlayTimerManager.OnTimerUpdated += UpdateTimerText;

        // 2. [신규] 게임 종료 이벤트(사망 & 탈출) 동시 구독
        MosquitoController.OnMosquitoDied += HideTimerUI;
        EscapeSystem.OnGameClear += HideTimerUI;
    }

    private void OnDisable()
    {
        // 메모리 누수 및 Dangling Event 방지를 위한 반사적 이벤트 해제
        PlayTimerManager.OnTimerUpdated -= UpdateTimerText;
        MosquitoController.OnMosquitoDied -= HideTimerUI;
        EscapeSystem.OnGameClear -= HideTimerUI;
    }

    /// <summary>
    /// 초 단위 시간을 받아 "MM:SS" 포맷의 텍스트로 변환 ($t_{\text{total}} \rightarrow \text{MM:SS}$)
    /// </summary>
    /// <param name="totalSeconds">경과 시간 (초 단위)</param>
    private void UpdateTimerText(float totalSeconds)
    {
        if (timerText == null) return;

        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);

        // "02:37" 형식 문자열 포매팅
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    /// <summary>
    /// 게임 종료 신호 수신 시 HUD 타이머 오브젝트를 화면에서 안전하게 숨김
    /// </summary>
    private void HideTimerUI()
    {
        Debug.Log("<color=cyan>[InGameTimerUI] 게임 종료 감지: HUD 타이머 비활성화</color>");

        // GameObject 자체를 꺼서 렌더링 및 UI 이벤트 레이어 연산을 완전 차단
        gameObject.SetActive(false);
    }
}