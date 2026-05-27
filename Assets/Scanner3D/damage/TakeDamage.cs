using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // Required for URP Volumes
using UnityEngine.Rendering.Universal; // Required for URP Effects

public class TakeDamage : MonoBehaviour
{
    public float intensity = 0;

    Volume _volume;
    Vignette _vignette;

    void Start()
    {
        _volume = GetComponent<Volume>();
        
        // URP uses TryGet instead of TryGetSettings
        if (_volume.profile.TryGet<Vignette>(out _vignette))
        {
            _vignette.active = false; 
        }
        else
        {
            print("No vignette found");
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            StartCoroutine(TakeDamageEffect());
        }
    }

    private IEnumerator TakeDamageEffect()
    {
        intensity = 0.4f;

        _vignette.active = true;
        _vignette.intensity.Override(0.4f);

        yield return new WaitForSeconds(0.4f);

        while (intensity > 0)
        {
            // Using Time.deltaTime for frame-independent smooth fading
            intensity -= Time.deltaTime * 2f; 

            if (intensity < 0) intensity = 0;

            _vignette.intensity.Override(intensity);

            yield return null; // Wait for the next frame
        }

        _vignette.active = false;
    }
}