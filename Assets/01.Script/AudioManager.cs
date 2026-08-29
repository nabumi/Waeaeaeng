using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전역 BGM, SFX, 모기 날갯짓 사운드를 통합 관리하는 싱글톤 오디오 매니저
/// (Resources.Load 기반 100% 보장 로드 및 씬 진입 시 BGM 자동 재생 지원)
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;
    public static AudioManager Instance
    {
        get
        {
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
        if (instance == null)
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
        MosquitoBuzz    // 날갯짓 윙윙 소리
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

    [Header("볼륨 설정")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1.0f;
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.7f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1.0f;

    private readonly Dictionary<SFXType, AudioClip> sfxClipMap = new Dictionary<SFXType, AudioClip>();

    private void Awake()
    {
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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CheckAndPlaySceneBGM(scene.name);
    }

    private void CheckAndPlaySceneBGM(string sceneName)
    {
        if (sceneName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0)
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
            bgmSource.spatialBlend = 0f; // 2D 사운드
            bgmSource.volume = bgmVolume * masterVolume;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f; // 2D 사운드
            sfxSource.volume = sfxVolume * masterVolume;
        }

        if (mosquitoBuzzSource == null)
        {
            mosquitoBuzzSource = gameObject.AddComponent<AudioSource>();
            mosquitoBuzzSource.loop = true;
            mosquitoBuzzSource.playOnAwake = false;
            mosquitoBuzzSource.spatialBlend = 0f; // 2D 사운드
            mosquitoBuzzSource.volume = 0.08f * sfxVolume * masterVolume; // 기존 0.4 대비 80% 감소
        }
    }

    private void LoadAudioClipsFromResources()
    {
        // Resources/Audio 폴더에서 클립 로드
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

        Debug.Log($"<color=green>[AudioManager] 오디오 클립 로드 완료 -> BGM: {(inGameBGM != null ? inGameBGM.name : "null")}, Slap: {(slapClip != null ? slapClip.name : "null")}, Dash: {(dashClip != null ? dashClip.name : "null")}, Buzz: {(mosquitoBuzzClip != null ? mosquitoBuzzClip.name : "null")}</color>");
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
    }

    public void PlayBGM(AudioClip clip, bool loop = true)
    {
        if (clip == null || bgmSource == null) return;
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.spatialBlend = 0f;
        bgmSource.volume = bgmVolume * masterVolume;
        bgmSource.Play();
        Debug.Log($"<color=cyan>[AudioManager] BGM 재생 시작: {clip.name}</color>");
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
    }

    public void PlaySFX(SFXType type, float volumeMultiplier = 1.0f, float pitch = 1.0f)
    {
        if (sfxClipMap.TryGetValue(type, out var clip) && clip != null)
        {
            PlaySFX(clip, volumeMultiplier, pitch);
        }
        else
        {
            Debug.LogWarning($"[AudioManager] SFXType '{type}'에 해당하는 오디오 클립이 없습니다.");
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
            mosquitoBuzzSource.volume = volume * sfxVolume * masterVolume;
            mosquitoBuzzSource.Play();
            Debug.Log("<color=cyan>[AudioManager] 모기 날갯짓 루프 재생 시작 (볼륨 0.08)</color>");
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
        if (bgmSource != null) bgmSource.volume = bgmVolume * masterVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume * masterVolume;
        AudioListener.volume = masterVolume;
    }

    public void ToggleMute(bool isMuted)
    {
        AudioListener.volume = isMuted ? 0f : masterVolume;
    }
}
