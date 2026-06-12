using UnityEngine;

public class DeliveryDestinationZone : MonoBehaviour
{
    private int orderId = -1;

    public void Initialize(int newOrderId)
    {
        orderId = newOrderId;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerDeliveryCarrier carrier =
            other.GetComponentInParent<PlayerDeliveryCarrier>();

        if (carrier == null)
        {
            return;
        }

        if (!carrier.HasCargo)
        {
            return;
        }

        DeliveryCargo cargo = carrier.CarriedCargo;

        if (cargo == null)
        {
            return;
        }

        if (DeliveryGameManager.Instance == null)
        {
            carrier.CompleteCurrentDelivery(orderId);
            return;
        }

        if (DeliveryGameManager.Instance.CanCompleteDelivery(cargo, orderId))
        {
            carrier.CompleteCurrentDelivery(orderId);
            return;
        }

        DeliveryGameManager.Instance.NotifyCargoMessage(
            "Wrong Food Delivered! No score change."
        );
    }
}
