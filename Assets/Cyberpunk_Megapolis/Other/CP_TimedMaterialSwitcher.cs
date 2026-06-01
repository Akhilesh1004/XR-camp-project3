using UnityEngine;

public class SimpleMaterialCycler : MonoBehaviour
{
    [Header("Target")]
    public Renderer target;
    [Tooltip("-1 = заменить во всех слотах, иначе индекс слота")]
    public int slotIndex = -1;

    [Header("Cycle")]
    public Material[] materials;     // >= 2
    public float interval = 1.0f;    // сек
    public bool changeOnStart = false;

    [Header("Advanced")]
    public bool useShared = false;   // TRUE = правим sharedMaterials, FALSE = инстансим (без влияния на другие объекты)

    int i;
    float t;
    Material[] runtimeMats;          // локальная копия/инстансы для безопасной замены
    bool ready;

    void Reset() => target = GetComponent<Renderer>();

    void Awake()
    {
        if (!target) target = GetComponent<Renderer>();
    }

    void OnEnable()
    {
        ready = (target && materials != null && materials.Length > 1);
        if (!ready) return;

        // 1) подготовим массив материалов РОВНО один раз
        var src = useShared ? target.sharedMaterials : target.materials; // .materials создает инстансы
        if (src == null || src.Length == 0) { ready = false; return; }

        runtimeMats = src; // копия/инстансы уже в src

        // 2) начальный материал
        i = 0;
        if (changeOnStart) Apply(materials[i]);

        // 3) таймер
        t = 0f;
    }

    void OnDisable()
    {
        runtimeMats = null;
        ready = false;
    }

    void Update()
    {
        if (!ready) return;

        t += Time.deltaTime;
        if (t < Mathf.Max(0.01f, interval)) return;
        t = 0f; // шаг строго раз в interval

        i = (i + 1) % materials.Length;
        Apply(materials[i]);
    }

    void Apply(Material m)
    {
        if (!m || runtimeMats == null) return;

        if (slotIndex < 0)
        {
            for (int k = 0; k < runtimeMats.Length; k++) runtimeMats[k] = m;
        }
        else
        {
            int idx = Mathf.Clamp(slotIndex, 0, runtimeMats.Length - 1);
            runtimeMats[idx] = m;
        }

        if (useShared)
            target.sharedMaterials = runtimeMats;  // правим shared (общие), может влиять на другие объекты
        else
            target.materials = runtimeMats;        // правим инстансы (без влияния на других)
    }
}
