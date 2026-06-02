using System.Collections.Generic;
using UnityEngine;

public class DroneEffectPool : MonoBehaviour
{
    public static DroneEffectPool Instance { get; private set; }

    [Header("Effect Pool")]
    public PooledEffect effectPrefab;

    [Tooltip("預先建立幾個爆炸特效，避免遊戲中 Instantiate 掉 FPS")]
    public int initialPoolSize = 12;

    [Tooltip("Pool 不夠時是否允許擴充。正式遊戲如果想完全避免 runtime Instantiate，可以關掉")]
    public bool allowExpansion = true;

    [Tooltip("同時播放的爆炸特效上限。超過時略過額外特效，避免粒子疊加造成 VR 掉幀。0 代表不限制。")]
    public int maxConcurrentEffects = 3;

    private readonly Queue<PooledEffect> pool = new Queue<PooledEffect>();
    private readonly HashSet<PooledEffect> pooledSet = new HashSet<PooledEffect>();
    private readonly HashSet<PooledEffect> playingSet = new HashSet<PooledEffect>();

    // Effects requested to return this frame.
    // We process them in LateUpdate, not inside OnDisable.
    private readonly List<PooledEffect> pendingReturnList = new List<PooledEffect>();
    private readonly HashSet<PooledEffect> pendingReturnSet = new HashSet<PooledEffect>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning("場上有多個 DroneEffectPool。自動尋找會使用第一個 Instance。");
        }

        Prewarm();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Prewarm()
    {
        if (effectPrefab == null)
        {
            Debug.LogWarning("DroneEffectPool: effectPrefab 沒有設定");
            return;
        }

        for (int i = 0; i < initialPoolSize; i++)
        {
            PooledEffect effect = Instantiate(effectPrefab, transform);
            effect.MarkAsInsidePool();

            if (effect.gameObject.activeSelf)
            {
                effect.gameObject.SetActive(false);
            }

            pool.Enqueue(effect);
            pooledSet.Add(effect);
        }
    }

    public PooledEffect Play(Vector3 position, Quaternion rotation)
    {
        if (effectPrefab == null)
        {
            return null;
        }

        if (maxConcurrentEffects > 0 && playingSet.Count >= maxConcurrentEffects)
        {
            return null;
        }

        PooledEffect effect = GetEffect();

        if (effect == null)
        {
            return null;
        }

        effect.MarkAsPlaying();
        playingSet.Add(effect);
        effect.PlayFromPool(this, position, rotation);

        return effect;
    }

    PooledEffect GetEffect()
    {
        while (pool.Count > 0)
        {
            PooledEffect effect = pool.Dequeue();

            if (effect == null)
            {
                continue;
            }

            pooledSet.Remove(effect);
            pendingReturnSet.Remove(effect);
            pendingReturnList.Remove(effect);

            return effect;
        }

        if (!allowExpansion)
        {
            return null;
        }

        PooledEffect newEffect = Instantiate(effectPrefab, transform);
        newEffect.MarkAsInsidePool();

        if (newEffect.gameObject.activeSelf)
        {
            newEffect.gameObject.SetActive(false);
        }

        return newEffect;
    }

    public void RequestReturnEffect(PooledEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        if (pooledSet.Contains(effect))
        {
            return;
        }

        if (pendingReturnSet.Contains(effect))
        {
            return;
        }

        pendingReturnSet.Add(effect);
        pendingReturnList.Add(effect);
    }

    void LateUpdate()
    {
        if (pendingReturnList.Count == 0)
        {
            return;
        }

        for (int i = 0; i < pendingReturnList.Count; i++)
        {
            PooledEffect effect = pendingReturnList[i];

            if (effect == null)
            {
                continue;
            }

            ReturnEffectNow(effect);
        }

        pendingReturnList.Clear();
        pendingReturnSet.Clear();
    }

    void ReturnEffectNow(PooledEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        if (pooledSet.Contains(effect))
        {
            return;
        }

        effect.MarkAsInsidePool();
        playingSet.Remove(effect);

        // Now we are outside OnDisable, so SetActive / SetParent is safe.
        if (effect.gameObject.activeSelf)
        {
            effect.gameObject.SetActive(false);
        }

        effect.transform.SetParent(transform, true);

        pool.Enqueue(effect);
        pooledSet.Add(effect);
    }
}
