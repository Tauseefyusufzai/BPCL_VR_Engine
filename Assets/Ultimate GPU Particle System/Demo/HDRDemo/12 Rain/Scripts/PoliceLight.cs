using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class PoliceLight : MonoBehaviour
{
	public float rotationSpeed = 1f;
	public float offset = 1f;

	public Light white1;
	public Light white2;

	public float whiteLightFlashSpeed = 1f;
	public float whiteLightIntensity = 1f;

    void Update()
    {
		transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);

		white1.intensity = Mathf.Sin(Time.realtimeSinceStartup * whiteLightFlashSpeed) * whiteLightIntensity;
		white2.intensity = Mathf.Cos(Time.realtimeSinceStartup * whiteLightFlashSpeed) * whiteLightIntensity;

	}
}
