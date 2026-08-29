using UnityEngine;
using System.Collections.Generic;

public class CharacterSpawner : MonoBehaviour
{
    [Header("Character Prefabs")]
    [Tooltip("월드에 생성할 5종의 캐릭터 프리팹 리스트")]
    [SerializeField] private List<GameObject> characterPrefabs;

    [Header("Spawn Position Settings")]
    [Tooltip("캐릭터가 생성될 침대 위 월드 좌표 Transform")]
    [SerializeField] private Transform spawnPoint;

    // 현재 생성된 캐릭터 인스턴스 메모리 캐싱 (GC 및 중복 생성 방지)
    private GameObject activeCharacterInstance;

    private void Start()
    {
        SpawnRandomCharacter();
    }

    /// <summary>
    /// 랜덤 캐릭터 생성 로직
    /// </summary>
    public void SpawnRandomCharacter()
    {
        if (characterPrefabs == null || characterPrefabs.Count == 0)
        {
            Debug.LogError("[Spawner] 등록된 캐릭터 프리팹이 없습니다!");
            return;
        }

        // 기존 캐릭터 메모리 해제
        if (activeCharacterInstance != null)
        {
            Destroy(activeCharacterInstance);
        }

        // 1. 균등 확률 랜덤 인덱스 추출 $P(X = i) = \frac{1}{N}$
        int randomIndex = Random.Range(0, characterPrefabs.Count);
        GameObject selectedPrefab = characterPrefabs[randomIndex];

        // 2. 위치 및 회전 설정
        Vector3 targetPosition = (spawnPoint != null) ? spawnPoint.position : Vector3.zero;
        Quaternion targetRotation = (spawnPoint != null) ? spawnPoint.rotation : Quaternion.identity;

        // 3. 월드에 생성 (Parent를 null로 두어 Canvas 스케일 영향을 받지 않도록 함)
        // Instantiate(Object original, Vector3 position, Quaternion rotation, Transform parent)
        activeCharacterInstance = Instantiate(selectedPrefab, targetPosition, targetRotation, null);

        // 4. 이름 정돈 (디버깅 편의성)
        activeCharacterInstance.name = $"Character_Instance_{randomIndex}";

        Debug.Log($"[Spawner] 월드 스페이스에 캐릭터 생성 성공: {activeCharacterInstance.name}");
    }

    // 시각적 디버깅: 에디터 씬 뷰에서 스폰 위치를 눈으로 확인
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 spawnPos = (spawnPoint != null) ? spawnPoint.position : transform.position;
        Gizmos.DrawWireSphere(spawnPos, 0.5f);
    }
}