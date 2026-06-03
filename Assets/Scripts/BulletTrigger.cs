using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class BulletTrigger : MonoBehaviour
{
    [Header("Layer 設定")]
    public LayerMask explodeOnCollisionLayer;
    public LayerMask damageLayer;
    public LayerMask destroyOnCollisionLayer;

    private ParticleSystem ps;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();    
    }

    void OnParticleTrigger()
    {
        if (ps == null) return;

        // 1. 建立儲存「進入」狀態粒子的清單
        List<ParticleSystem.Particle> enterParticles = new List<ParticleSystem.Particle>();

        // 2. 獲取進入 Trigger 的粒子數量與 ColliderData 資訊
        int numEnter = ps.GetTriggerParticles(
            ParticleSystemTriggerEventType.Enter, 
            enterParticles, 
            out ParticleSystem.ColliderData colliderData
        );

        // 遍歷所有撞擊成功的粒子
        for (int i = 0; i < numEnter; i++)
        {
            // 🌟 修正：將變數型態改為 Component，避免型態轉換失敗的編譯錯誤
            Component targetComponent = colliderData.GetCollider(i, 0);

            if (targetComponent != null)
            {
                // 透過 Component 順利取得 gameObject 進行後續判定
                GameObject hitObj = targetComponent.gameObject;
                int hitLayer = hitObj.layer;

                if (hitObj.TryGetComponent<DroneNPC>(out DroneNPC DroneNPCScript))
                {
                    Debug.Log("撞到了掛有 DroneNPC 腳本的物件：" + hitObj.name);

                    if (IsInLayerMask(hitLayer, explodeOnCollisionLayer))
                    {
                        DroneNPCScript.Explode(); 
                        break; 
                    }
                }
                else if (hitObj.TryGetComponent<DroneNPC2>(out DroneNPC2 DroneNPCScript2))
                {
                    Debug.Log("撞到了掛有 DroneNPC2 腳本的物件：" + hitObj.name);

                    if (IsInLayerMask(hitLayer, damageLayer))
                    {
                        DroneNPCScript2.TakeDamage(1); 
                        continue; 
                    }

                    if (IsInLayerMask(hitLayer, destroyOnCollisionLayer))
                    {
                        DroneNPCScript2.DestroyByDamage(); 
                    }
                }
            }
        }
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}