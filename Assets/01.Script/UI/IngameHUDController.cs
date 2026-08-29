using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Realtime Ingame HUD Controller for Timer and Blood Counter
/// Automatically initializes, binds, and updates Time and Blood UI in Ingame scene
/// </summary>
public class IngameHUDController : MonoBehaviour
{
    public static IngameHUDController Instance { get; private set; }

    public enum BloodDisplayType
    {
        CurrentBlood,       // Current remaining blood (e.g. 40ml)
        TotalSuckedBlood,   // Cumulative sucked blood (e.g. 0ml -> 150ml)
        CurrentAndEscape    // Current / Escape Target (e.g. 40 / 150ml)
    }

    [Header("Timer UI")]
    [SerializeField] private TextMeshProUGUI timeTmpText;
    [SerializeField] private Text timeLegacyText;
    [SerializeField] private string timePrefix = "Time ";

    [Header("Blood UI")]
    [SerializeField] private TextMeshProUGUI bloodTmpText;
    [SerializeField] private Text bloodLegacyText;
    [SerializeField] private BloodDisplayType bloodDisplayType = BloodDisplayType.CurrentBlood;
    [SerializeField] private bool appendUnit = true;

    private float lastDisplayedTime = -1f;
    private float lastDisplayedBlood = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitializeOnSceneLoad()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0) return;

        EnsureHUDExists();
    }

    public static IngameHUDController EnsureHUDExists()
    {
        if (Instance != null) return Instance;

        var existing = FindAnyObjectByType<IngameHUDController>();
        if (existing != null)
        {
            Instance = existing;
            existing.BindComponents();
            return existing;
        }

        // Find main canvas
        Canvas targetCanvas = null;
        var allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var c in allCanvases)
        {
            if (c.gameObject.scene.isLoaded && c.renderMode != RenderMode.WorldSpace)
            {
                targetCanvas = c;
                break;
            }
        }

        GameObject host = targetCanvas != null ? targetCanvas.gameObject : null;
        if (host == null)
        {
            host = new GameObject("[IngameHUDController]");
        }

        var hud = host.GetComponent<IngameHUDController>() ?? host.AddComponent<IngameHUDController>();
        Instance = hud;
        hud.BindComponents();
        return hud;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        BindComponents();
    }

    private void Start()
    {
        if (Instance == null) Instance = this;
        BindComponents();
        RefreshHUD(true);
    }

    private void OnEnable()
    {
        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.OnBloodAmountChanged += HandleBloodAmountChanged;
            BloodManager.Instance.OnBloodSucked += HandleBloodSucked;
        }
    }

    private void OnDisable()
    {
        if (BloodManager.Instance != null)
        {
            BloodManager.Instance.OnBloodAmountChanged -= HandleBloodAmountChanged;
            BloodManager.Instance.OnBloodSucked -= HandleBloodSucked;
        }
    }

    private void Update()
    {
        if (timeTmpText == null && timeLegacyText == null) BindTimeComponent();
        if (bloodTmpText == null && bloodLegacyText == null) BindBloodComponent();

        UpdateTimerHUD();
        UpdateBloodHUD();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BindComponents();
    }
#endif

    public void BindComponents()
    {
        BindTimeComponent();
        BindBloodComponent();
    }

    private void BindTimeComponent()
    {
        if (timeTmpText != null || timeLegacyText != null) return;

        var t = FindChildRecursive(transform, "Time", "Timer");
        if (t != null)
        {
            timeTmpText = t.GetComponentInChildren<TextMeshProUGUI>();
            timeLegacyText = t.GetComponentInChildren<Text>();
        }

        if (timeTmpText == null && timeLegacyText == null)
        {
            var allTmps = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            foreach (var tmp in allTmps)
            {
                if (!tmp.gameObject.scene.isLoaded) continue;
                if (tmp.name.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (tmp.transform.parent != null && tmp.transform.parent.name.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    tmp.text.IndexOf("TIme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tmp.text.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    timeTmpText = tmp;
                    break;
                }
            }
        }

        if (timeTmpText != null)
        {
            Debug.Log($"[IngameHUDController] Time UI bound: {timeTmpText.gameObject.name}");
        }
    }

    private void BindBloodComponent()
    {
        if (bloodTmpText != null || bloodLegacyText != null) return;

        var b = FindChildRecursive(transform, "Blood", "Score");
        if (b != null)
        {
            bloodTmpText = b.GetComponentInChildren<TextMeshProUGUI>();
            bloodLegacyText = b.GetComponentInChildren<Text>();
        }

        if (bloodTmpText == null && bloodLegacyText == null)
        {
            var allTmps = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            foreach (var tmp in allTmps)
            {
                if (!tmp.gameObject.scene.isLoaded) continue;
                if (tmp == timeTmpText) continue;
                if (tmp.name.IndexOf("Blood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (tmp.transform.parent != null && tmp.transform.parent.name.IndexOf("Blood", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    tmp.text.IndexOf("10000", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bloodTmpText = tmp;
                    break;
                }
            }
        }

        if (bloodTmpText != null)
        {
            Debug.Log($"[IngameHUDController] Blood UI bound: {bloodTmpText.gameObject.name}");
        }
    }

    private Transform FindChildRecursive(Transform parent, params string[] candidateNames)
    {
        foreach (Transform child in parent)
        {
            foreach (var name in candidateNames)
            {
                if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }

            var sub = FindChildRecursive(child, candidateNames);
            if (sub != null) return sub;
        }
        return null;
    }

    private void HandleBloodAmountChanged(float current, float max)
    {
        UpdateBloodHUD();
    }

    private void HandleBloodSucked(float suckedThisTime, float totalSucked)
    {
        UpdateBloodHUD();
    }

    private void UpdateTimerHUD()
    {
        float survivalSec = BloodManager.Instance != null ? BloodManager.Instance.SurvivalTime : Time.timeSinceLevelLoad;
        int totalSec = Mathf.Max(0, (int)survivalSec);

        if (totalSec != (int)lastDisplayedTime || lastDisplayedTime < 0f)
        {
            lastDisplayedTime = totalSec;
            int minutes = totalSec / 60;
            int seconds = totalSec % 60;
            string formattedTime = $"{timePrefix}{minutes:00}:{seconds:00}";

            if (timeTmpText != null) timeTmpText.text = formattedTime;
            if (timeLegacyText != null) timeLegacyText.text = formattedTime;
        }
    }

    private void UpdateBloodHUD()
    {
        float currentVal = 0f;
        string formattedText = "";

        if (BloodManager.Instance != null)
        {
            switch (bloodDisplayType)
            {
                case BloodDisplayType.CurrentBlood:
                    currentVal = BloodManager.Instance.CurrentBlood;
                    formattedText = appendUnit ? $"{Mathf.FloorToInt(currentVal)}ml" : $"{Mathf.FloorToInt(currentVal)}";
                    break;

                case BloodDisplayType.TotalSuckedBlood:
                    currentVal = BloodManager.Instance.TotalSuckedBlood;
                    formattedText = appendUnit ? $"{Mathf.FloorToInt(currentVal)}ml" : $"{Mathf.FloorToInt(currentVal)}";
                    break;

                case BloodDisplayType.CurrentAndEscape:
                    currentVal = BloodManager.Instance.CurrentBlood;
                    float threshold = BloodManager.Instance.EscapeThresholdBlood;
                    formattedText = appendUnit
                        ? $"{Mathf.FloorToInt(currentVal)} / {(int)threshold}ml"
                        : $"{Mathf.FloorToInt(currentVal)} / {(int)threshold}";
                    break;
            }
        }
        else
        {
            formattedText = appendUnit ? "40ml" : "40";
        }

        if (Mathf.Abs(currentVal - lastDisplayedBlood) > 0.01f || lastDisplayedBlood < 0f ||
            (bloodTmpText != null && bloodTmpText.text != formattedText))
        {
            lastDisplayedBlood = currentVal;
            if (bloodTmpText != null) bloodTmpText.text = formattedText;
            if (bloodLegacyText != null) bloodLegacyText.text = formattedText;
        }
    }

    public void RefreshHUD(bool force = false)
    {
        if (force)
        {
            lastDisplayedTime = -1f;
            lastDisplayedBlood = -1f;
        }
        UpdateTimerHUD();
        UpdateBloodHUD();
    }
}
