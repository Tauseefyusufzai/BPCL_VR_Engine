using UnityEngine;

public class FanRotation : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Speed of rotation in degrees per second")]
    public float rotationSpeed = 360f;
    
    [Tooltip("Axis of rotation (default: Z-axis for ceiling fan)")]
    public Vector3 rotationAxis = Vector3.forward;
    
    [Header("Control")]
    [Tooltip("Enable/disable fan rotation")]
    public bool isRotating = true;
    
    void Update()
    {
        if (isRotating)
        {
            transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
        }
    }
    
    public void ToggleFan()
    {
        isRotating = !isRotating;
    }
    
    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = speed;
    }
    
    public void StartFan()
    {
        isRotating = true;
    }
    
    public void StopFan()
    {
        isRotating = false;
    }
}
