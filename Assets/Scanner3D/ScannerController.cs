using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScannerController : MonoBehaviour
{
     [Header("Speed")]
    public float scanSpeed = 2f;

    [Header("Destroy Time")]
    public float delay_destroy_time = 3f;

    void Start()
    {
        Destroy(gameObject, delay_destroy_time);
    }

    void Update()
    {
        float growing = scanSpeed * Time.deltaTime;
        transform.localScale += new Vector3(growing, growing, growing);
    }
}