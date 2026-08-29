// 파일명: ZoneType.cs
namespace GlobalEnums // (네임스페이스 없이 전역으로 작성하셔도 됩니다)
{
    /// <summary>
    /// 신체 부위 구역의 위험도 종류 정의
    /// </summary>
    public enum ZoneType
    {
        Green,  // 그린존: 안전 (최대 10)
        Yellow, // 옐로우존: 보통 (최대 15)
        Red     // 레드존: 위험 (최대 20)
    }
}