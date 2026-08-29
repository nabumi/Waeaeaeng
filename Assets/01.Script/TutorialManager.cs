using UnityEngine;
using UnityEngine.UI;
using System;

public class TutorialManager : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private GameObject tutorialPanel; // 튜토리얼 패널 UI
    [SerializeField] private Button closeButton;     // 닫기/확인 버튼

    [Header("Settings")]
    [Tooltip("체크 시 게임 시작마다 항상 튜토리얼을 띄웁니다.")]
    [SerializeField] private bool alwaysShowTutorial = true;

    // 튜토리얼이 완료되었을 때 플레이어 조작 해제 등을 처리할 이벤트 (저결합 설계)
    public static event Action OnTutorialCompleted;

    private void Awake()
    {
        // 버튼 이벤트 바인딩
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
    /// 게임 시작 시 튜토리얼 UI를 출력합니다.
    /// </summary>
    private void CheckAndShowTutorial()
    {
        if (alwaysShowTutorial)
        {
            ShowTutorial();
        }
        else
        {
            int hasSeen = PlayerPrefs.GetInt("HasSeenTutorial_v1", 0);
            if (hasSeen == 0)
            {
                ShowTutorial();
            }
            else
            {
                if (tutorialPanel != null) tutorialPanel.SetActive(false);
                Time.timeScale = 1f;
            }
        }
    }

    /// <summary>
    /// 튜토리얼 창을 열고 시간을 멈춥니다.
    /// </summary>
    public void ShowTutorial()
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(true);
            tutorialPanel.transform.SetAsLastSibling();
        }
        Time.timeScale = 0f;
        Debug.Log("<color=cyan>[TutorialManager] 튜토리얼 패널 오픈 (Time.timeScale = 0)</color>");
    }

    /// <summary>
    /// 튜토리얼을 닫고 게임을 시작합니다.
    /// </summary>
    public void CloseTutorial()
    {
        PlayerPrefs.SetInt("HasSeenTutorial_v1", 1);
        PlayerPrefs.Save();

        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(false);
        }
        Time.timeScale = 1f;

        OnTutorialCompleted?.Invoke();
        Debug.Log("<color=green>[TutorialManager] 튜토리얼 닫힘 -> 게임 시작 (Time.timeScale = 1)</color>");
    }
}