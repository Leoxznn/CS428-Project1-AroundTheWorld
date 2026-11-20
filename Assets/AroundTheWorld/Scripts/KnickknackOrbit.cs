using UnityEngine;

// Put this on an empty pivot; children placed at an offset will circle around the pivot.
public class KnickknackOrbit : MonoBehaviour
{
    public float degreesPerSecond = 25f;
    public float verticalWave = 0.004f;
    public float wavePeriod = 4f;
    Vector3 startPos;
    void Start() { startPos = transform.localPosition; }
    void Update()
    {
        transform.Rotate(0, degreesPerSecond * Time.deltaTime, 0, Space.Self);
        transform.localPosition = startPos + Vector3.up * Mathf.Sin(Time.time * Mathf.PI * 2f / wavePeriod) * verticalWave;
    }
}

