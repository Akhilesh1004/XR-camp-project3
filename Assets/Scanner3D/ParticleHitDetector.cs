using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleHitDetector : MonoBehaviour
{
    // Start is called before the first frame update
     void OnParticleCollision(GameObject Cube)
    {
        Debug.Log("Particle Collision Detected with " + Cube.name);
    }
}
