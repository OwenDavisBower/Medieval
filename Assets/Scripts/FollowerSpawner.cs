using UnityEngine;

public class FollowerSpawner : MonoBehaviour
{
    [SerializeField] FollowerController followerPrefab;
    [SerializeField] int followerCount = 5;
    [SerializeField] float spawnRingRadius = 3f;

    void Start()
    {
        if (followerPrefab == null)
            return;

        for (int i = 0; i < followerCount; i++)
        {
            float angle = i * (Mathf.PI * 2f / followerCount);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * spawnRingRadius;
            Vector3 pos = transform.position + offset + Vector3.up * 0.05f;

            FollowerController follower = Instantiate(followerPrefab, pos, Quaternion.identity);
            follower.Initialize(i);
        }
    }
}
