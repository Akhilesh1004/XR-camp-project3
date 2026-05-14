using UnityEngine;

public class DestroyOnContact : MonoBehaviour
{
    [Header("設定目標圖層")]
    [Tooltip("選擇碰到哪些 Layer 後物件會消失 (可以複選)")]
    public LayerMask targetLayers;

    private void OnCollisionEnter(Collision collision)
    {
        if ((targetLayers.value & (1 << collision.gameObject.layer)) > 0)
        {
            Disappear();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((targetLayers.value & (1 << other.gameObject.layer)) > 0)
        {
            Disappear();
        }
    }

    private void Disappear()
    {
        Destroy(gameObject);
        // 如果你未來使用物件池 (Object Pooling) 來優化效能，
        // 可以把上面那行註解掉，改成下面這行來隱藏物件：
        // gameObject.SetActive(false);
    }
}