using UnityEngine;

public class KnickknackSpin : MonoBehaviour
{
    public Vector3 degreesPerSecond = new Vector3(0, 30, 0);
    void Update() { transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self); }
}

