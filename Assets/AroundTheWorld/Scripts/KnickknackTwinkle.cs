using UnityEngine;

// Random sparkle: scales the object (and optionally a light) up and down.
public class KnickknackTwinkle : MonoBehaviour
{
    public float minScale = 0.2f;
    public float maxScale = 1.2f;
    public float speed = 6f;
    public Light linkedLight;
    public float maxIntensity = 0.6f;
    Vector3 baseScale;
    float seed;
    void Start() { baseScale = transform.localScale; seed = Random.value * 100f; }
    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed);
        n = n * n * n * 2.5f; // short bright flashes
        float k = Mathf.Lerp(minScale, maxScale, Mathf.Clamp01(n));
        transform.localScale = baseScale * k;
        if (linkedLight != null) linkedLight.intensity = maxIntensity * Mathf.Clamp01(n);
    }
}
