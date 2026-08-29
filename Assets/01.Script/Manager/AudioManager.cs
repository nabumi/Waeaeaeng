using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전역 BGM, SFX, 모기 날갯짓 사운드를 통합 관리하는 싱글톤 오디오 매니저
/// (씬 내 수동 배치가 없어도 호출 시 자동 인스턴스화 및 오디오 에셋 자동 바인딩 지원)
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
                }
            }
            return instance;
        }
    }

    public enum SFXType
    {
        Slap,           // 손바닥 강타 슬랩 소리
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
    [SerializeField] private AudioClip qteSuccessClip;
    [SerializeField] private AudioClip qteGreatClip;
    [SerializeField] private AudioClip qteFailClip;
    [SerializeField] private AudioClip bloodSuckClip;
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField] private AudioClip mosquitoBuzzClip;

    [Header("볼륨 설정")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1.0f;
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.6f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1.0f;

    private readonly Dictionary<SFXType, AudioClip> sfxClipMap = new Dictionary<SFXType, AudioClip>();

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAudioSources();
            AutoBindAudioClips();
            RegisterClips();
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // 현재 씬이 인게임 씬이면 인게임 BGM 자동 재생
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene.IndexOf("Ingame", StringComparison.OrdinalIgnoreCase) >= 0)
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
            bgmSource.volume = bgmVolume * masterVolume;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.volume = sfxVolume * masterVolume;
        }

        if (mosquitoBuzzSource == null)
        {
            mosquitoBuzzSource = gameObject.AddComponent<AudioSource>();
            mosquitoBuzzSource.loop = true;
            mosquitoBuzzSource.playOnAwake = false;
            mosquitoBuzzSource.volume = 0.35f * sfxVolume * masterVolume;
        }
    }

    private void AutoBindAudioClips()
    {
        AudioClip[] allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
        var clipMap = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in allClips)
        {
            if (c != null && !clipMap.ContainsKey(c.name))
            {
                clipMap[c.name] = c;
            }
        }

        AudioClip FindClip(params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                foreach (var kvp in clipMap)
                {
                    if (kvp.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return kvp.Value;
                    }
                }
            }
            return null;
        }

        if (inGameBGM == null) inGameBGM = FindClip("bgm_ingame", "lobbybgm");
        if (lobbyBGM == null) lobbyBGM = FindClip("lobbybgm");
        if (slapClip == null) slapClip = FindClip("alex_jauk-slap", "slap");
        if (qteSuccessClip == null) qteSuccessClip = FindClip("sfx_qte_success");
        if (qteGreatClip == null) qteGreatClip = FindClip("sfx_qte_great");
        if (qteFailClip == null) qteFailClip = FindClip("sfx_qte_fail");
        if (bloodSuckClip == null) bloodSuckClip = FindClip("sfx_blood_suck");
        if (gameOverClip == null) gameOverClip = FindClip("sfx_gameover");
        if (mosquitoBuzzClip == null) mosquitoBuzzClip = FindClip("freesound_community-single-mosquito-buzz", "lobbymoskito");
    }

    private void RegisterClips()
    {
        if (slapClip != null) sfxClipMap[SFXType.Slap] = slapClip;
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
    }

    public void PlaySFX(AudioClip clip, float volumeMultiplier = 1.0f, float pitch = 1.0f)
    {
        if (clip == null || sfxSource == null) return;

        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, sfxVolume * masterVolume * volumeMultiplier);
    }

    public void StartMosquitoBuzz(float volume = 0.35f)
    {
        if (mosquitoBuzzSource == null) return;
        if (mosquitoBuzzClip != null && mosquitoBuzzSource.clip != mosquitoBuzzClip)
        {
            mosquitoBuzzSource.clip = mosquitoBuzzClip;
        }

        if (!mosquitoBuzzSource.isPlaying && mosquitoBuzzSource.clip != null)
        {
            mosquitoBuzzSource.volume = volume * sfxVolume * masterVolume;
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
        if (bgmSource != null) bgmSource.volume = bgmVolume * masterVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume * masterVolume;
        AudioListener.volume = masterVolume;
    }

    public void ToggleMute(bool isMuted)
    {
        AudioListener.volume = isMuted ? 0f : masterVolume;
    }
}
