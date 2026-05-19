using UnityEngine;

public class DeliveryDamageSource : MonoBehaviour
{
    [Header("餐點傷害設定")]
    public int damage = 20;
    public float radius = 3f;
    public LayerMask playerLayer;

    [Tooltip("爆炸 prefab 啟用時自動造成傷害")]
    public bool applyOnEnable = true;

    [Tooltip("避免 explosion prefab 重複啟用時重複傷害")]
    public bool applyOnlyOnce = true;

    private bool hasApplied = false;

    void OnEnable()
    {
        if (applyOnEnable)
        {
            ApplyDamage();
        }
    }

    public void ApplyDamage()
    {
        if (applyOnlyOnce && hasApplied)
        {
            return;
        }

        hasApplied = true;

        DamageCarriedCargoInRadius(
            transform.position,
            radius,
            damage,
            playerLayer
        );
    }

    public static void DamageCarriedCargoInRadius(
        Vector3 center,
        float radius,
        int damage,
        LayerMask playerLayer
    )
    {
        if (damage <= 0 || radius <= 0f)
        {
            return;
        }

        Collider[] hits;

        if (playerLayer.value != 0)
        {
            hits = Physics.OverlapSphere(
                center,
                radius,
                playerLayer,
                QueryTriggerInteraction.Ignore
            );
        }
        else
        {
            hits = Physics.OverlapSphere(
                center,
                radius,
                ~0,
                QueryTriggerInteraction.Ignore
            );
        }

        foreach (Collider hit in hits)
        {
            PlayerDeliveryCarrier carrier =
                hit.GetComponentInParent<PlayerDeliveryCarrier>();

            if (carrier == null)
            {
                continue;
            }

            carrier.DamageCarriedCargo(damage);
            return;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}