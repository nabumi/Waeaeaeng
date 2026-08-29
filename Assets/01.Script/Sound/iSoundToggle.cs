using UnityEngine;

public class SoundToggle : MonoBehaviour
{
    public GameObject soundOnImage;
    public GameObject soundOffImage;

    private bool isSoundOn;

    void Start()
    {
        isSoundOn = AudioListener.volume > 0f;
        UpdateUI();
    }

    public void ToggleSound()
    {
        isSoundOn = !isSoundOn;

        AudioListener.volume = isSoundOn ? 1f : 0f;

        UpdateUI();
    }

    private void UpdateUI()
    {
        soundOnImage.SetActive(isSoundOn);
        soundOffImage.SetActive(!isSoundOn);
    }
}