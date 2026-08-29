using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 앱 최초 실행 또는 타이틀 씬 진입 시에만 튜토리얼을 출력하고,
/// 인게임 재시작(Retry) 시에는 튜토리얼을 자동으로 스킵하는 최적화 매니저.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private GameObject tutorialPanel; // 튜토리얼 패널 UI
    [SerializeField] private Button closeButton;       // 닫기/확인 버튼

    // C# 정적 변수: 씬이 재로드(Retry)되어도 메모리에 보존됨 ($O(1)$ 연산)
    // 앱이 완전히 껏다 켜지거나(Domain Reload) 타이틀에서 리셋을 호출할 때만 true가 됨
    private static bool isFirstStartFromTitle = true;

    // 튜토리얼 완료 및 게임 진행 개시 이벤트 (플레이어 이동 해제 등)
    public static event Action OnTutorialCompleted;

    private void Awake()
    {
        // 버튼 이벤트 리스너 바인딩 (중복 등록 방지)
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseTutorial);
        }
    }

    private void Start()
    {
        CheckAndShowTutorial();
    }

    /// <summary>
    /// 진입 상태(타이틀 진입 vs 인게임 재시작)를 검사하여 튜토리얼 노출 여부를 결정합니다.
    /// </summary>
    private void CheckAndShowTutorial()
    {
        if (isFirstStartFromTitle)
        {
            // [경로 1] 앱 최초 실행 또는 타이틀을 통해 진입했을 때 -> 튜토리얼 출력
            ShowTutorial();

            // 플래그를 false로 전환 (이후 인게임에서 씬을 아무리 재로드/Retry 해도 스킵됨)
            isFirstStartFromTitle = false;
        }
        else
        {
            // [경로 2] 인게임 사망/클리어 후 재시작(Retry) -> 튜토리얼 즉시 스킵
            SkipTutorial();
        }
    }

    /// <summary>
    /// 튜토리얼 창을 열고 게임 세상을 일시정지합니다. ($Time.timeScale = 0$)
    /// </summary>
    public void ShowTutorial()
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(true);
        }

        // 시간 정지 연산 ($Time.timeScale = 0$)
        Time.timeScale = 0f;

        Debug.Log("<color=cyan>[TutorialManager]</color> 최초 진입: 튜토리얼 표시 및 게임 일시정지");
    }

    /// <summary>
    /// 재시작(Retry) 유저를 위해 튜토리얼을 띄우지 않고 곧바로 게임을 시작합니다.
    /// </summary>
    private void SkipTutorial()
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(false);
        }

        // 게임 시간 정상화 ($Time.timeScale = 1$)
        Time.timeScale = 1f;

        // 플레이어 모기 조작 해제 이벤트를 즉시 호출하여 바로 플레이 가능하게 처리
        OnTutorialCompleted?.Invoke();

        Debug.Log("<color=yellow>[TutorialManager]</color> 재시작(Retry) 감지: 튜토리얼 스킵 후 즉시 게임 시작!");
    }

    /// <summary>
    /// 튜토리얼을 닫고 게임 세상을 재개합니다.
    /// </summary>
    public void CloseTutorial()
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(false);
        }

        // 시간 재개 ($Time.timeScale = 1$)
        Time.timeScale = 1f;

        // 이벤트 전송
        OnTutorialCompleted?.Invoke();

        Debug.Log("<color=green>[TutorialManager]</color> 튜토리얼 완료 및 게임 개시!");
    }

    /// <summary>
    /// [외부 연동용 API] 타이틀 씬의 '게임 시작' 버튼이나 TitleManager에서 호출해 주는 정적 메서드.
    /// 메인 메뉴를 거쳐서 다시 올 때는 튜토리얼을 다시 보여주도록 플래그를 리셋합니다.
    /// </summary>
    public static void ResetTutorialFlag()
    {
        isFirstStartFromTitle = true;
        Debug.Log("<color=magenta>[TutorialManager]</color> 타이틀 씬 진입 확인: 튜토리얼 출력 플래그 초기화 완료");
    }
}