using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class GyroscopeTracking : MonoBehaviour
{
    private AttitudeSensor _sensor;
    private bool _active = false;

    public void StartTracking()
    {
        _sensor = AttitudeSensor.current;
        
        if (_sensor == null)
        {
            Debug.LogError("AttitudeSensor no disponible");
            return;
        }

        InputSystem.EnableDevice(_sensor);
        _active = true;
        Debug.Log("AttitudeSensor iniciado: " + _sensor.name);
    }

    public void StopTracking()
    {
        _active = false;
        if (_sensor != null)
            InputSystem.DisableDevice(_sensor);
        transform.localRotation = Quaternion.identity;
    }

    void Update()
    {
        if (!_active || _sensor == null) return;

        Quaternion att = _sensor.attitude.ReadValue();
        
        // Conversión al sistema de coordenadas de Unity
        Quaternion rot = new Quaternion(att.x, att.y, -att.z, -att.w);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f) * rot;

        if (Time.frameCount % 60 == 0)
            Debug.Log($"Attitude: {att.eulerAngles}");
    }
}