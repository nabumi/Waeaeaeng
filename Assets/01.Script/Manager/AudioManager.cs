using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전역 BGM, SFX, 모기 날갯짓 사운드를 통합 관리하는 싱글톤 오디오 매니저
/// (Resources.Load 기반 100% 보장 로드, 씬 전환 시 자동 사운드 정단 및 BGM 전환 지원)
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;
    private static bool isQuitting = false; // 🛑 앱 종료/씬 언로드 시 유령 생성 방지 플래그

    public static AudioManager Instance
    {
        get
        {
            // 씬이 언로드되거나 앱 종료 중이라면 절대로 새로 생성하지 않음
            if (isQuitting)
            {
                Debug.LogWarning("[AudioManager] 앱 종료 또는 씬 이동 중으로 인스턴스 생성을 차단합니다.");
                return null;
            }

            if (instance == null)
            {
                instance = FindAnyObjectByType<AudioManager>();
                if (instance == null)
                {
                    var go = new GameObject("[AudioManager]");
                    instance = go.AddComponent<AudioManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (!isQuitting && instance == null)
        {
            var inst = Instance; // 인스턴스 자동 생성
        }
    }

    public enum SFXType
    {
        Slap,           // 손바닥 강타 슬랩 소리
        Dash,           // 대쉬/회피 바람 가르는 소리
        QteSuccess,     // QTE 일반 성공 벨 사운드
        QteGreat,       // QTE 대성공 차임 사운드
        QteFail,        // QTE 실패 부저 사운드
        BloodSuck,      // 흡혈 꼴깍 사운드
        GameOver,       // 사망/게임오버 사운드
        MosquitoBuzz,   // 날갯짓 윙윙 소리
        EscapeReady,    // 탈출 가능 알림 사운드
        Victory         // 승리/클리어 사운드
    }

    [Header("오디오 소스")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource mosquitoBuzzSource;

    [Header("BGM 클립")]
    [SerializeField] private AudioClip inGameBGM;
    [SerializeField] private AudioClip lobbyBGM;

    [Header("SFX 클립 매핑")]
    [SerializeField] private AudioClip slapClip;
    [SerializeField] private AudioClip dashClip;
    [SerializeField] private AudioClip qteSuccessClip;
    [SerializeField] private AudioClip qteGreatClip;
    [SerializeField] private AudioClip qteFailClip;
    [SerializeField] private AudioClip bloodSuckClip;
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField] private AudioClip mosquitoBuzzClip;
    [SerializeField] private AudioClip escapeReadyClip;
    [SerializeField] private AudioClip victoryClip;

    [Header("볼륨 설정")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1.0f;
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.7f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1.0f;

    private readonly Dictionary<SFXType, AudioClip> sfxClipMap = new Dictionary<SFXType, AudioClip>();

    private void Awake()
    {
        // ---------------------------------------------------------
        // 1. [핵심] 중복 생성 방지 및 즉각적인 소음 차단
        // ---------------------------------------------------------
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
            LoadAudioClipsFromResources();
            RegisterClips();

            if (AudioListener.volume <= 0.001f)
            {
                AudioListener.volume = 1.0f;
            }
        }
        else if (instance != this)
        {
            // 중복 생성된 객체라면 즉시 모든 오디오 소스를 끄고 파괴 (소리 중복 방지)
            AudioSource[] sources = GetComponents<AudioSource>();
            foreach (var src in sources) src.enabled = false;

            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        CheckAndPlaySceneBGM(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// 씬이 로드될 때마다 호출되는 콜백 (청소 + BGM 전환)
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ---------------------------------------------------------
        // 2. [핵심] 씬 이동 시 인게임 잔여 사운드 강제 정지 (청소 로직)
        // ---------------------------------------------------------
        StopMosquitoBuzz(); // 모기 날갯짓 소리 즉시 정지
        if (sfxSource != null) sfxSource.Stop(); // 진행 중이던 단발성 SFX 정지

        // 씬 이름에 따른 BGM 전환
        CheckAndPlaySceneBGM(scene.name);
    }

    private void CheckAndPlaySceneBGM(string sceneName)
    {
        if (sceneName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PlayLobbyBGM();
        }
        else if (sceneName.IndexOf("Ingame", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PlayInGameBGM();
        }
    }

    private void InitializeAudioSources()
    {
        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.volume = bgmVolume * masterVolume;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.volume = sfxVolume * masterVolume;
        }

        if (mosquitoBuzzSource == null)
        {
            mosquitoBuzzSource = gameObject.AddComponent<AudioSource>();
            mosquitoBuzzSource.loop = true;
            mosquitoBuzzSource.playOnAwake = false;
            mosquitoBuzzSource.spatialBlend = 0f;
            mosquitoBuzzSource.volume = GetCalculatedMosquitoVolume();
        }
    }

    private void LoadAudioClipsFromResources()
    {
        if (inGameBGM == null) inGameBGM = Resources.Load<AudioClip>("Audio/bgm_ingame");
        if (lobbyBGM == null) lobbyBGM = Resources.Load<AudioClip>("Audio/lobbybgm");
        if (slapClip == null) slapClip = Resources.Load<AudioClip>("Audio/alex_jauk-slap-237622");
        if (dashClip == null) dashClip = Resources.Load<AudioClip>("Audio/sfx_dash");
        if (qteSuccessClip == null) qteSuccessClip = Resources.Load<AudioClip>("Audio/sfx_qte_success");
        if (qteGreatClip == null) qteGreatClip = Resources.Load<AudioClip>("Audio/sfx_qte_great");
        if (qteFailClip == null) qteFailClip = Resources.Load<AudioClip>("Audio/sfx_qte_fail");
        if (bloodSuckClip == null) bloodSuckClip = Resources.Load<AudioClip>("Audio/sfx_blood_suck");
        if (gameOverClip == null) gameOverClip = Resources.Load<AudioClip>("Audio/sfx_gameover");
        if (mosquitoBuzzClip == null) mosquitoBuzzClip = Resources.Load<AudioClip>("Audio/freesound_community-single-mosquito-buzz-69360");
        if (escapeReadyClip == null) escapeReadyClip = Resources.Load<AudioClip>("Audio/sfx_escape_ready");
        if (victoryClip == null) victoryClip = Resources.Load<AudioClip>("Audio/sfx_victory");
    }

    private void RegisterClips()
    {
        sfxClipMap.Clear();
        if (slapClip != null) sfxClipMap[SFXType.Slap] = slapClip;
        if (dashClip != null) sfxClipMap[SFXType.Dash] = dashClip;
        if (qteSuccessClip != null) sfxClipMap[SFXType.QteSuccess] = qteSuccessClip;
        if (qteGreatClip != null) sfxClipMap[SFXType.QteGreat] = qteGreatClip;
        if (qteFailClip != null) sfxClipMap[SFXType.QteFail] = qteFailClip;
        if (bloodSuckClip != null) sfxClipMap[SFXType.BloodSuck] = bloodSuckClip;
        if (gameOverClip != null) sfxClipMap[SFXType.GameOver] = gameOverClip;
        if (mosquitoBuzzClip != null) sfxClipMap[SFXType.MosquitoBuzz] = mosquitoBuzzClip;
        if (escapeReadyClip != null) sfxClipMap[SFXType.EscapeReady] = escapeReadyClip;
        if (victoryClip != null) sfxClipMap[SFXType.Victory] = victoryClip;
    }

    public void PlayBGM(AudioClip clip, bool loop = true)
    {
        if (clip == null || bgmSource == null) return;

        // 이미 동일한 BGM이 재생 중이라면 리셋 없이 유지
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.spatialBlend = 0f;
        bgmSource.volume = bgmVolume * masterVolume;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource != null) bgmSource.Stop();
    }

    public void PlayInGameBGM()
    {
        if (inGameBGM != null) PlayBGM(inGameBGM);
        else Debug.LogWarning("[AudioManager] inGameBGM 클립이 비어있습니다!");
    }

    public void PlayLobbyBGM()
    {
        if (lobbyBGM != null) PlayBGM(lobbyBGM);
        else Debug.LogWarning("[AudioManager] lobbyBGM 클립이 비어있습니다!");
    }

    public void PlaySFX(SFXType type, float volumeMultiplier = 1.0f, float pitch = 1.0f)
    {
        if (sfxClipMap.TryGetValue(type, out var clip) && clip != null)
        {
            PlaySFX(clip, volumeMultiplier, pitch);
        }
    }

    public void PlaySFX(AudioClip clip, float volumeMultiplier = 1.0f, float pitch = 1.0f)
    {
        if (clip == null || sfxSource == null) return;

        sfxSource.pitch = pitch;
        sfxSource.spatialBlend = 0f;
        sfxSource.PlayOneShot(clip, sfxVolume * masterVolume * volumeMultiplier);
    }

    public void StartMosquitoBuzz(float volume = 0.08f)
    {
        if (mosquitoBuzzSource == null) return;
        if (mosquitoBuzzClip != null && mosquitoBuzzSource.clip != mosquitoBuzzClip)
        {
            mosquitoBuzzSource.clip = mosquitoBuzzClip;
        }

        if (!mosquitoBuzzSource.isPlaying && mosquitoBuzzSource.clip != null)
        {
            mosquitoBuzzSource.spatialBlend = 0f;
            mosquitoBuzzSource.volume = GetCalculatedMosquitoVolume(volume);
            mosquitoBuzzSource.Play();
        }
    }

    public void StopMosquitoBuzz()
    {
        if (mosquitoBuzzSource != null && mosquitoBuzzSource.isPlaying)
        {
            mosquitoBuzzSource.Stop();
        }
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateSourceVolumes();
        AudioListener.volume = masterVolume;
    }

    public void SetBGMVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        UpdateSourceVolumes();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        UpdateSourceVolumes();
    }

    private void UpdateSourceVolumes()
    {
        if (bgmSource != null) bgmSource.volume = bgmVolume * masterVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume * masterVolume;
        if (mosquitoBuzzSource != null) mosquitoBuzzSource.volume = GetCalculatedMosquitoVolume();
    }

    /// <summary>
    /// 모기 사운드 최종 볼륨 계산 공식
    /// $V_{\text{Mosquito}} = \text{baseVolume} \times V_{\text{SFX}} \times V_{\text{Master}}$
    /// </summary>
    private float GetCalculatedMosquitoVolume(float baseVolume = 0.08f)
    {
        return baseVolume * sfxVolume * masterVolume;
    }

    public void ToggleMute(bool isMuted)
    {
        AudioListener.volume = isMuted ? 0f : masterVolume;
        AudioListener.pause = isMuted;
        if (bgmSource != null) bgmSource.mute = isMuted;
        if (sfxSource != null) sfxSource.mute = isMuted;
        if (mosquitoBuzzSource != null) mosquitoBuzzSource.mute = isMuted;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            isQuitting = true;
        }
    }

    public float GetMasterVolume() => masterVolume;
    public float GetBGMVolume() => bgmVolume;
    public float GetSFXVolume() => sfxVolume;
    public bool IsMuted => AudioListener.volume <= 0.001f;
}