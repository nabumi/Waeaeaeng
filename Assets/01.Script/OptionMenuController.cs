using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;

/// <summary>
/// Scrollbar 컴포넌트를 이용해 AudioMixer 볼륨을 제어하는 오디오 옵션 컨트롤러 Class
/// </summary>
public class OptionMenuController : MonoBehaviour
{
    [Header("Audio Mixer Reference")]
    [Tooltip("프로젝트의 Main AudioMixer 자산")]
    [SerializeField] private AudioMixer mainAudioMixer;

    [Header("Audio Mixer Parameter Names")]
    private const string MIXER_MASTER = "MasterVol";
    private const string MIXER_BGM = "BgmVol";
    private const string MIXER_SFX = "SfxVol";

    [Header("PlayerPrefs Keys")]
    private const string PREF_MASTER_MUTE = "Pref_MasterMute";
    private const string PREF_BGM_VOL = "Pref_BgmVol";
    private const string PREF_SFX_VOL = "Pref_SfxVol";

    [Header("UI Scrollbar Bindings")]
    [Tooltip("Master Mute 토글 버튼 (sample 1 또는 sample 3의 Toggle)")]
    [SerializeField] private Toggle masterMuteToggle;

    [Tooltip("배경음악 스크롤바 (sample 2 하위의 Scrollbar)")]
    [SerializeField] private Scrollbar bgmScrollbar;

    [Tooltip("효과음 스크롤바 (sample 3 하위의 Scrollbar)")]
    [SerializeField] private Scrollbar sfxScrollbar;

    private bool isMuted = false;
    private float lastBgmVolume = 0.8f;
    private float lastSfxVolume = 0.8f;

    private void Awake()
    {
        // 1. Scrollbar 이벤트 동적 바인딩
        if (bgmScrollbar != null)
        {
            bgmScrollbar.onValueChanged.AddListener(OnBgmScrollbarChanged);
        }

        if (sfxScrollbar != null)
        {
            sfxScrollbar.onValueChanged.AddListener(OnSfxScrollbarChanged);
        }

        // 2. Toggle 이벤트 동적 바인딩
        if (masterMuteToggle != null)
        {
            masterMuteToggle.onValueChanged.AddListener(OnMasterMuteToggled);
        }
    }

    private void Start()
    {
        // 저장된 설정 로드 및 UI 초기화
        LoadOptionSettings();
    }

    /// <summary>
    /// PlayerPrefs에서 볼륨 데이터를 읽어와 Scrollbar 및 AudioMixer 동기화
    /// </summary>
    private void LoadOptionSettings()
    {
        isMuted = PlayerPrefs.GetInt(PREF_MASTER_MUTE, 0) == 1;
        lastBgmVolume = PlayerPrefs.GetFloat(PREF_BGM_VOL, 0.8f);
        lastSfxVolume = PlayerPrefs.GetFloat(PREF_SFX_VOL, 0.8f);

        // Toggle 및 Scrollbar UI 상태 반영
        if (masterMuteToggle != null) masterMuteToggle.isOn = isMuted;
        if (bgmScrollbar != null) bgmScrollbar.value = lastBgmVolume;
        if (sfxScrollbar != null) sfxScrollbar.value = lastSfxVolume;

        ApplyAudioVolumes();
    }

    /// <summary>
    /// BGM 스크롤바 값 변경 콜백 함수
    /// </summary>
    public void OnBgmScrollbarChanged(float value)
    {
        lastBgmVolume = value;
        PlayerPrefs.SetFloat(PREF_BGM_VOL, value);
        PlayerPrefs.Save();

        if (!isMuted)
        {
            SetMixerVolume(MIXER_BGM, value);
        }
    }

    /// <summary>
    /// SFX 스크롤바 값 변경 콜백 함수
    /// </summary>
    public void OnSfxScrollbarChanged(float value)
    {
        lastSfxVolume = value;
        PlayerPrefs.SetFloat(PREF_SFX_VOL, value);
        PlayerPrefs.Save();

        if (!isMuted)
        {
            SetMixerVolume(MIXER_SFX, value);
        }
    }

    /// <summary>
    /// 마스터 음소거 토글 변경 콜백 함수
    /// </summary>
    public void OnMasterMuteToggled(bool isMute)
    {
        isMuted = isMute;
        PlayerPrefs.SetInt(PREF_MASTER_MUTE, isMuted ? 1 : 0);
        PlayerPrefs.Save();

        ApplyAudioVolumes();
    }

    /// <summary>
    /// 선형 비율(0.0001~1.0)을 데시벨($dB$) 공식으로 환산하여 오디오 믹서에 적용
    /// 수학 공식: $dB = 20 \times \log_{10}(v)$
    /// </summary>
    private void SetMixerVolume(string parameterName, float linearValue)
    {
        if (mainAudioMixer == null) return;

        // $0$ 값 입력 시 $\log_{10}(0) = -\infty$ 예외를 방지하기 위해 최소값을 $0.0001f$로 클램핑
        float clampedValue = Mathf.Clamp(linearValue, 0.0001f, 1f);
        float dB = Mathf.Log10(clampedValue) * 20f;

        mainAudioMixer.SetFloat(parameterName, dB);
    }

    /// <summary>
    /// 현재 Mute 상태에 맞춰 AudioMixer 전체 볼륨 일괄 업데이트
    /// </summary>
    private void ApplyAudioVolumes()
    {
        if (mainAudioMixer == null) return;

        if (isMuted)
        {
            mainAudioMixer.SetFloat(MIXER_MASTER, -80f); // 완전 음소거
        }
        else
        {
            mainAudioMixer.SetFloat(MIXER_MASTER, 0f);    // 음소거 해제
            SetMixerVolume(MIXER_BGM, lastBgmVolume);
            SetMixerVolume(MIXER_SFX, lastSfxVolume);
        }
    }
}