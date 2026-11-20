using UnityEngine;

public class KnickknackBob : MonoBehaviour
{
    public float amplitude = 0.002f;
    public float period = 3f;
    public float rollDegrees = 4f;
    Vector3 startPos;
    Quaternion startRot;
    void Start() { startPos = transform.localPosition; startRot = transform.localRotation; }
    void Update()
    {
        float t = Time.time * Mathf.PI * 2f / period;
        transform.localPosition = startPos + Vector3.up * Mathf.Sin(t) * amplitude;
        transform.localRotation = startRot * Quaternion.Euler(Mathf.Sin(t * 0.7f) * rollDegrees * 0.5f, 0, Mathf.Sin(t) * rollDegrees);
    }
}

