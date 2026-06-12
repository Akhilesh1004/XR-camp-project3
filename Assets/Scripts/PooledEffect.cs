using UnityEngine;

public class PooledEffect : MonoBehaviour
{
    [Header("Fallback Return")]
    [Tooltip("如果 ParticleSystem Stop Action 沒有 Disable，或只有子 ParticleSystem 被 Disable，就用時間自動回收 root")]
    public bool useFallbackTimer = true;

    [Tooltip("Fallback 回收時間。要大於整個爆炸特效最長播放時間")]
    public float fallbackLifeTime = 3f;

    [Header("Replay Settings")]
    [Tooltip("重播時自動把 prefab 底下被 Stop Action = Disable 關掉的子物件重新啟用")]
    public bool reactivateChildrenOnPlay = true;

    private DroneEffectPool ownerPool;
    private float fallbackReturnTime;

    private bool isPlayingFromPool = false;
    private bool isInsidePool = true;
    private Transform[] cachedTransforms;
    private ParticleSystem[] cachedParticleSystems;
    private AudioSource[] cachedAudioSources;
    private DeliveryDamageSource[] cachedDamageSources;

    public bool IsPlayingFromPool
    {
        get { return isPlayingFromPool; }
    }

    public bool IsInsidePool
    {
        get { return isInsidePool; }
    }

    void Awake()
    {
        CacheComponents();
    }

    public void PlayFromPool(
        DroneEffectPool pool,
        Vector3 position,
        Quaternion rotation
    )
    {
        ownerPool = pool;

        isInsidePool = false;
        isPlayingFromPool = true;

        transform.SetParent(null, true);
        transform.position = position;
        transform.rotation = rotation;

        ResetDamageSourcesForReuse();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (reactivateChildrenOnPlay)
        {
            ReactivateParticleChildren();
        }

        CacheComponents();
        ApplyAutoDamageSourcesIfNeeded();

        foreach (ParticleSystem ps in cachedParticleSystems)
        {
            if (!ps.gameObject.activeSelf)
            {
                ps.gameObject.SetActive(true);
            }

            ps.Clear(true);
            ps.Play(true);
        }

        foreach (AudioSource audio in cachedAudioSources)
        {
            if (!audio.gameObject.activeSelf)
            {
                audio.gameObject.SetActive(true);
            }

            audio.Stop();
            audio.Play();
        }

        fallbackReturnTime = Time.time + fallbackLifeTime;
    }

    void ReactivateParticleChildren()
    {
        CacheComponents();

        foreach (Transform child in cachedTransforms)
        {
            if (child == transform)
            {
                continue;
            }

            if (!child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(true);
            }
        }
    }

    void CacheComponents()
    {
        if (cachedTransforms != null &&
            cachedParticleSystems != null &&
            cachedAudioSources != null &&
            cachedDamageSources != null)
        {
            return;
        }

        cachedTransforms = GetComponentsInChildren<Transform>(true);
        cachedParticleSystems = GetComponentsInChildren<ParticleSystem>(true);
        cachedAudioSources = GetComponentsInChildren<AudioSource>(true);
        cachedDamageSources = GetComponentsInChildren<DeliveryDamageSource>(true);
    }

    void ResetDamageSourcesForReuse()
    {
        CacheComponents();

        foreach (DeliveryDamageSource damageSource in cachedDamageSources)
        {
            if (damageSource != null)
            {
                damageSource.ResetForReuse();
            }
        }
    }

    public bool HasAutoApplyDeliveryDamageSource()
    {
        CacheComponents();

        foreach (DeliveryDamageSource damageSource in cachedDamageSources)
        {
            if (damageSource != null &&
                damageSource.enabled &&
                damageSource.applyOnEnable)
            {
                return true;
            }
        }

        return false;
    }

    void ApplyAutoDamageSourcesIfNeeded()
    {
        CacheComponents();

        foreach (DeliveryDamageSource damageSource in cachedDamageSources)
        {
            if (damageSource == null ||
                !damageSource.enabled ||
                !damageSource.applyOnEnable ||
                damageSource.HasApplied)
            {
                continue;
            }

            damageSource.ApplyDamage();
        }
    }

    void Update()
    {
        if (!isPlayingFromPool)
        {
            return;
        }

        if (!useFallbackTimer)
        {
            return;
        }

        if (Time.time >= fallbackReturnTime)
        {
            RequestReturnToPool();
        }
    }

    void OnDisable()
    {
        // Root ParticleSystem Stop Action = Disable 會讓 root GameObject 進入停用流程。
        // 這裡不要 SetActive(false)、不要 SetParent。
        // 只通知 Pool：下一幀再安全回收，避免 Unity 報
        // "GameObject is already being activated or deactivated"。
        if (isPlayingFromPool && !isInsidePool)
        {
            RequestReturnToPool();
        }
    }

    public void RequestReturnToPool()
    {
        if (!isPlayingFromPool && isInsidePool)
        {
            return;
        }

        if (ownerPool != null)
        {
            ownerPool.RequestReturnEffect(this);
        }
        else
        {
            MarkAsInsidePool();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }

    public void MarkAsPlaying()
    {
        isPlayingFromPool = true;
        isInsidePool = false;
    }

    public void MarkAsInsidePool()
    {
        isPlayingFromPool = false;
        isInsidePool = true;
    }
}
