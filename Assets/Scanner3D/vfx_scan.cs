using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class vfx_scan : MonoBehaviour
{
    public GameObject TerrainScannerPrefab;
    public float scanDuration = 10;
    public float scanRadius = 500;

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnTerrainScanner();
        }
    }
    void SpawnTerrainScanner()
    {
        GameObject terrainScanner = Instantiate(TerrainScannerPrefab, gameObject.transform.position, Quaternion.identity) as GameObject;
        ParticleSystem terrainScannerPS = terrainScanner.GetComponent<ParticleSystem>();
        
        if (terrainScannerPS != null)
        {
            var main = terrainScannerPS.main;
            main.duration = scanDuration;
            main.startLifetime = scanDuration;
            main.startSize = scanRadius * 2; // Diameter of the scan area
        }
        else
        
            Debug.LogError("TerrainScannerPrefab does not have a ParticleSystem component.");
        Destroy(terrainScanner, scanDuration + 1); // Destroy the scanner after it finishes
    }
}