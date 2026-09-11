using UnityEngine;

public class CameraShake : MonoBehaviour
{
    private Vector3 originalPosition;
    private float shakeAmount = 0;
    private float shakeDuration = 0;

    void Update()
    {
        if (shakeDuration > 0)
        {
            transform.localPosition = originalPosition + Random.insideUnitSphere * shakeAmount;
            shakeDuration -= Time.deltaTime;
        }
        else
        {
            transform.localPosition = originalPosition;
        }
    }

    public void Shake(float amount, float duration)
    {
        originalPosition = transform.localPosition;
        shakeAmount = amount;
        shakeDuration = duration;
    }
}
