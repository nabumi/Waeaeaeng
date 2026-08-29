using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ESC 키 입력 감지, 일시정지(Pause), 볼륨 조절 및 로비 이동을 총괄하는 옵션 패널 UI 컨트롤러
/// </summary>
public class OptionUIController : MonoBehaviour
{
    public static OptionUIController Instance { get; private set; }

    [Header("패널 루트")]
    [SerializeField] private GameObject panelRoot;

    [Header("마스터 볼륨 바인딩")]
    [SerializeField] private Scrollbar masterVolumeScrollbar;
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle masterMuteToggle;

    [Header("BGM 볼륨 바인딩")]
    [SerializeField] private Scrollbar bgmVolumeScrollbar;
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Toggle bgmMuteToggle;

    [Header("SFX 볼륨 바인딩")]
    [SerializeField] private Scrollbar sfxVolumeScrollbar;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Toggle sfxMuteToggle;

    [Header("버튼 바인딩")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button quitButton;

    private bool isPaused = false;
    private bool isSyncingUI = false;
    private float lastToggleTime = -1f;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        BindEvents();
    }

    private void Start()
    {
        // 시작 시 패널 닫기 및 시간 정상화
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
        isPaused = false;
    }

    private void Update()
    {
        CheckEscapeInput();
    }

    private void CheckEscapeInput()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        // 동일 프레임 또는 0.25초 이내 중복 호출 방지
        if (Time.unscaledTime - lastToggleTime < 0.25f) return;
        lastToggleTime = Time.unscaledTime;

        // 게임 진행 중이 아닐 때(사망 또는 클리어)는 일시정지 무시
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            OpenPausePanel();
        }
    }

    public void OpenPausePanel()
    {
        isPaused = true;
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
        }
        Time.timeScale = 0f;

        SyncUIWithAudioSettings();
        Debug.Log("<color=cyan>[OptionUIController] 게임 일시정지 (옵션창 활성화)</color>");
    }

    public void ResumeGame()
    {
        isPaused = false;
        if (panelRoot != null) panelRoot.SetActive(false);
        Time.timeScale = 1.0f;
        Debug.Log("<color=cyan>[OptionUIController] 게임 재개 (옵션창 비활성화)</color>");
    }

    public void OnLobbyClicked()
    {
        Time.timeScale = 1.0f;
        isPaused = false;
        if (panelRoot != null) panelRoot.SetActive(false);

        AudioManager.Instance?.PlayLobbyBGM();
        SceneManager.LoadScene("Title");
        Debug.Log("<color=green>[OptionUIController] 로비(Title) 씬으로 이동</color>");
    }

    public void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BindEvents()
    {
        // 마스터 볼륨
        if (masterVolumeScrollbar != null)
            masterVolumeScrollbar.onValueChanged.AddListener(OnMasterScrollbarChanged);
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterSliderChanged);
        if (masterMuteToggle != null)
            masterMuteToggle.onValueChanged.AddListener(OnMasterMuteChanged);

        // BGM 볼륨
        if (bgmVolumeScrollbar != null)
            bgmVolumeScrollbar.onValueChanged.AddListener(OnBGMScrollbarChanged);
        if (bgmVolumeSlider != null)
            bgmVolumeSlider.onValueChanged.AddListener(OnBGMSliderChanged);
        if (bgmMuteToggle != null)
            bgmMuteToggle.onValueChanged.AddListener(OnBGMMuteChanged);

        // SFX 볼륨
        if (sfxVolumeScrollbar != null)
            sfxVolumeScrollbar.onValueChanged.AddListener(OnSFXScrollbarChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXSliderChanged);
        if (sfxMuteToggle != null)
            sfxMuteToggle.onValueChanged.AddListener(OnSFXMuteChanged);

        // 버튼
        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeGame);
        if (lobbyButton != null)
            lobbyButton.onClick.AddListener(OnLobbyClicked);
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void SyncUIWithAudioSettings()
    {
        if (AudioManager.Instance == null) return;

        isSyncingUI = true;

        float masterVol = AudioManager.Instance.GetMasterVolume();
        float bgmVol = AudioManager.Instance.GetBGMVolume();
        float sfxVol = AudioManager.Instance.GetSFXVolume();
        bool isMuted = AudioManager.Instance.IsMuted;

        if (masterVolumeScrollbar != null) masterVolumeScrollbar.value = masterVol;
        if (masterVolumeSlider != null) masterVolumeSlider.value = masterVol;
        if (masterMuteToggle != null) masterMuteToggle.isOn = !isMuted;

        if (bgmVolumeScrollbar != null) bgmVolumeScrollbar.value = bgmVol;
        if (bgmVolumeSlider != null) bgmVolumeSlider.value = bgmVol;
        if (bgmMuteToggle != null) bgmMuteToggle.isOn = bgmVol > 0.001f;

        if (sfxVolumeScrollbar != null) sfxVolumeScrollbar.value = sfxVol;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfxVol;
        if (sfxMuteToggle != null) sfxMuteToggle.isOn = sfxVol > 0.001f;

        isSyncingUI = false;
    }

    private void OnMasterScrollbarChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetMasterVolume(value);
    }

    private void OnMasterSliderChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetMasterVolume(value);
    }

    private void OnMasterMuteChanged(bool isOn)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.ToggleMute(!isOn);
    }

    private void OnBGMScrollbarChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetBGMVolume(value);
    }

    private void OnBGMSliderChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetBGMVolume(value);
    }

    private void OnBGMMuteChanged(bool isOn)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetBGMVolume(isOn ? 0.7f : 0f);
    }

    private void OnSFXScrollbarChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetSFXVolume(value);
    }

    private void OnSFXSliderChanged(float value)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetSFXVolume(value);
    }

    private void OnSFXMuteChanged(bool isOn)
    {
        if (isSyncingUI) return;
        AudioManager.Instance?.SetSFXVolume(isOn ? 1.0f : 0f);
    }
}
