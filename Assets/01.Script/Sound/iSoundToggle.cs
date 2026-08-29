using UnityEngine;
using UnityEngine.UI;

public class SoundToggle : MonoBehaviour
{
    [SerializeField] public GameObject soundOnImage;
    [SerializeField] public GameObject soundOffImage;

    private Button button;
    private bool isSoundOn = true;
    private float lastToggleTime = -1f;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void Start()
    {
        isSoundOn = !AudioListener.pause && AudioListener.volume > 0.001f;
        UpdateUI();
    }

    public void ToggleSound()
    {
        // 동일 프레임 또는 0.2초 이내 중복 호출 방지 (인스펙터와 코드 리스너 이중 호출 방지)
        if (Time.unscaledTime - lastToggleTime < 0.2f) return;
        lastToggleTime = Time.unscaledTime;

        isSoundOn = !isSoundOn;

        AudioListener.pause = !isSoundOn;
        AudioListener.volume = isSoundOn ? 1.0f : 0.0f;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.ToggleMute(!isSoundOn);
        }

        var allSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
        foreach (var src in allSources)
        {
            if (src != null) src.mute = !isSoundOn;
        }

        UpdateUI();
        Debug.Log($"<color=cyan>[SoundToggle] 사운드 토글: {(isSoundOn ? "ON" : "OFF")} (AudioListener.pause: {AudioListener.pause}, volume: {AudioListener.volume})</color>");
    }

    private void UpdateUI()
    {
        if (soundOnImage != null) soundOnImage.SetActive(isSoundOn);
        if (soundOffImage != null) soundOffImage.SetActive(!isSoundOn);
    }
}
