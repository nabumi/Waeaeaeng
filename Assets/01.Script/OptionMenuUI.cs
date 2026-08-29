using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// ESC 키 입력 및 옵션/일시정지 UI 창을 전역으로 제어하는 UI 매니저 Class
/// </summary>
public class OptionMenuUI : MonoBehaviour
{
    public static OptionMenuUI Instance { get; private set; }

    [Header("UI Reference")]
    [Tooltip("ESC 누를 때 켜고 끌 옵션 패널 UI")]
    [SerializeField] private GameObject optionPanel;

    // 일시정지 상태 변경 이벤트 (isPaused)
    public static event Action<bool> OnPauseStateChanged;

    private bool isPaused = false;
    public bool IsPaused => isPaused;

    // 대시(Bullet Time) 연출 보호를 위한 이전 TimeScale 저장 변수
    private float previousTimeScale = 1.0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (optionPanel != null)
        {
            optionPanel.SetActive(false);
        }
    }

    private void Update()
    {
        // New Input System을 통한 ESC 키 입력 글로벌 감지
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleOptionMenu();
        }
    }

    /// <summary>
    /// 옵션 메뉴 토글 및 게임 일시정지/재개 처리
    /// </summary>
    public void ToggleOptionMenu()
    {
        isPaused = !isPaused;

        if (optionPanel != null)
        {
            optionPanel.SetActive(isPaused);
        }

        if (isPaused)
        {
            PauseGame();
        }
        else
        {
            ResumeGame();
        }

        // 구독 중인 다른 시스템(모기 컨트롤러, 사운드 매니저 등)에 상태 알림
        OnPauseStateChanged?.Invoke(isPaused);
    }

    private void PauseGame()
    {
        // 현재 timeScale 백업 (대시 스킬 작동 중일 때 $Time.timeScale = 0.2$ 등을 보존)
        previousTimeScale = Time.timeScale;

        // 시간 정지
        Time.timeScale = 0f;

        Debug.Log("<color=cyan>[OptionMenuUI]</color> 게임 일시정지 (ESC)");
    }

    private void ResumeGame()
    {
        // 이전 시간 속도로 복원
        Time.timeScale = previousTimeScale;

        Debug.Log("<color=green>[OptionMenuUI]</color> 게임 재개 (ESC)");
    }

    // UI 버튼(Continue/Close)에서 클릭 이벤트로 연결할 메서드
    public void OnClickCloseButton()
    {
        if (isPaused)
        {
            ToggleOptionMenu();
        }
    }
}