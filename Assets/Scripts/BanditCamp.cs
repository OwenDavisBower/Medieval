using UnityEngine;

public class BanditCamp : MonoBehaviour
{
    [SerializeField] BanditController banditPrefab;
    [SerializeField] int banditCount = 3;
    [SerializeField] float spawnRadiusMin = 1f;
    [SerializeField] float spawnRadiusMax = 4f;

    void Start()
    {
        if (banditPrefab == null)
            return;

        for (int i = 0; i < banditCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(spawnRadiusMin, spawnRadiusMax);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = transform.position + offset + Vector3.up * 0.05f;

            BanditController bandit = Instantiate(banditPrefab, pos, Quaternion.identity);
            bandit.Initialize(transform);
        }
    }
}
