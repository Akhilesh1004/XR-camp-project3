using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class MaterialIntensityController : MonoBehaviour
{
    [Header("時間設定")]
    [Tooltip("等待 X 秒後才開始變亮與變白")]
    public float delayTime = 2.0f;
    [Tooltip("開始執行後，花費 N 秒完成變化")]
    public float duration = 3.0f;

    [Header("材質發光設定")]
    public float startIntensity = -10f;
    public float endIntensity = 10f;

    [Header("VR 視野設定")]
    public Image whiteScreenImage;

    private Renderer targetRenderer;
    private Material targetMaterial;
    private Color baseColor;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        targetRenderer = GetComponent<Renderer>();
        targetMaterial = targetRenderer.material;
        targetMaterial.EnableKeyword("_EMISSION");

        baseColor = targetMaterial.GetColor(EmissionColorID);
        if (baseColor == Color.black) baseColor = Color.white;

        // 確保剛開始畫面與材質強度是初始狀態
        if (whiteScreenImage != null)
        {
            Color c = whiteScreenImage.color;
            c.a = 0f;
            whiteScreenImage.color = c;
        }
        
        // 初始材質強度設為 startIntensity (-10)
        float initialFactor = Mathf.Pow(2f, startIntensity);
        targetMaterial.SetColor(EmissionColorID, baseColor * initialFactor);

        // 啟動協程（內部會先等待 delayTime 秒）
        StartCoroutine(GlowAndWhiteOutRoutine(duration));
    }

    IEnumerator GlowAndWhiteOutRoutine(float time)
    {
        // 【關鍵修改】：讓程式在這裡靜止等待 X 秒
        yield return new WaitForSeconds(delayTime);

        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / time; // 進度比例 (0 到 1)

            // 1. 同步處理：物體發光強度變更 (-10 到 10)
            float currentIntensity = Mathf.Lerp(startIntensity, endIntensity, t);
            float factor = Mathf.Pow(2f, currentIntensity);
            targetMaterial.SetColor(EmissionColorID, baseColor * factor);

            // 2. 同步處理：整個視野漸進變白 (Alpha 從 0 到 1)
            if (whiteScreenImage != null)
            {
                Color screenColor = whiteScreenImage.color;
                screenColor.a = t; // 隨著時間 t，透明度從 0% 變成 100%
                whiteScreenImage.color = screenColor;
            }

            yield return null;
        }

        // 確保最後精確停在最大值
        float finalFactor = Mathf.Pow(2f, endIntensity);
        targetMaterial.SetColor(EmissionColorID, baseColor * finalFactor);

        if (whiteScreenImage != null)
        {
            Color finalScreenColor = whiteScreenImage.color;
            finalScreenColor.a = 1f;
            whiteScreenImage.color = finalScreenColor;
        }
    }

    private void OnDestroy()
    {
        if (targetMaterial != null) Destroy(targetMaterial);
    }
}