using UnityEngine;
using UnityEngine.UI;
using System;

public class TutorialManager : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private GameObject tutorialPanel; // 튜토리얼 패널 UI
    [SerializeField] private Button closeButton;     // 닫기/확인 버튼

    [Header("Settings")]
    // GC(가비지 컬렉션)를 줄이기 위한 Key 상수 정의
    private const string TUTORIAL_KEY = "HasSeenTutorial_v1";

    // 튜토리얼이 완료되었을 때 플레이어 조작 해제 등을 처리할 이벤트 (저결합 설계)
    public static event Action OnTutorialCompleted;

    private void Awake()
    {
        // 버튼 이벤트 바인딩
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseTutorial);
        }
    }

    private void Start()
    {
        CheckAndShowTutorial();
    }

    /// <summary>
    /// 최초 진입 여부를 검사하고 UI를 출력합니다.
    /// </summary>
    private void CheckAndShowTutorial()
    {
        // PlayerPrefs.GetInt(Key, DefaultValue): 저장된 값이 없으면 기본값 0을 반환합니다.
        int hasSeen = PlayerPrefs.GetInt(TUTORIAL_KEY, 0);

        if (hasSeen == 0)
        {
            // [최초 실행] 튜토리얼 출력 및 게임 시간 일시정지
            ShowTutorial();
        }
        else
        {
            // [이미 봄] 튜토리얼 패널 비활성화 및 게임 진행
            tutorialPanel.SetActive(false);
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// 튜토리얼 창을 열고 시간을 멈춥니다. (외부 옵션 메뉴 등에서도 재호출 가능)
    /// </summary>
    public void ShowTutorial()
    {
        tutorialPanel.SetActive(true);
        Time.timeScale = 0f; // $Time.timeScale = 0$ 세상을 멈춘다!
    }

    /// <summary>
    /// 튜토리얼을 닫고 플레이 기록을 저장합니다.
    /// </summary>
    public void CloseTutorial()
    {
        // 1. 상태 저장 (1: 이미 봄)
        PlayerPrefs.SetInt(TUTORIAL_KEY, 1);
        PlayerPrefs.Save(); // Disk에 확실히 기록

        // 2. UI 비활성화 및 시간 재개
        tutorialPanel.SetActive(false);
        Time.timeScale = 1f;

        // 3. 외부 시스템(플레이어 이동 등)에 알림
        OnTutorialCompleted?.Invoke();

        Debug.Log("<color=green>[TutorialManager]</color> 튜토리얼 완료 처리 및 데이터 저장 완료!");
    }
}