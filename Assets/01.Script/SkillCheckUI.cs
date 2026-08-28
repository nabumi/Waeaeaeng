using System;
using UnityEngine;

public class SkillCheckUI : MonoBehaviour
{
    public static SkillCheckUI Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform barBackground;
    [SerializeField] private RectTransform needle;
    [SerializeField] private RectTransform successZone;

    private bool isRunning = false;
    private float currentSpeed = 600f;
    private float currentNeedleX = 0f;
    private Action<bool> onComplete;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (!isRunning) return;

        // 바늘 왕복 이동 연산: $P(t) = \text{PingPong}(t \cdot v, W) - \frac{W}{2}$
        float barWidth = barBackground.rect.width;
        float pingPong = Mathf.PingPong(Time.time * currentSpeed, barWidth);
        currentNeedleX = pingPong - (barWidth * 0.5f);

        needle.anchoredPosition = new Vector2(currentNeedleX, needle.anchoredPosition.y);
    }

    /// <summary>
    /// PlayerInput의 OnCheck()로부터 신호를 받아 바늘 위치를 판정
    /// </summary>
    public void OnInputPressed()
    {
        if (!isRunning) return;

        isRunning = false;

        // 성공 구역 판정 범위 계산
        float zoneWidth = successZone.rect.width;
        float zoneCenterX = successZone.anchoredPosition.x;
        float minX = zoneCenterX - (zoneWidth * 0.5f);
        float maxX = zoneCenterX + (zoneWidth * 0.5f);

        bool isSuccess = (currentNeedleX >= minX && currentNeedleX <= maxX);

        canvasGroup.alpha = 0f; // UI 숨김
        onComplete?.Invoke(isSuccess);
    }

    public void BeginSkillCheck(float speedMultiplier, Action<bool> callback)
    {
        onComplete = callback;
        currentSpeed = 500f * speedMultiplier;
        canvasGroup.alpha = 1f; // UI 표시
        isRunning = true;
    }
}