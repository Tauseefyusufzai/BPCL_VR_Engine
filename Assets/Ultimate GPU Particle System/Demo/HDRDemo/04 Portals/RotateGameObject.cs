using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class RotateGameObject : MonoBehaviour
{
	public Vector3 rotationSpeed = new Vector3(0f,0f,1f);

	void Update ()
	{
		transform.Rotate(rotationSpeed * Time.deltaTime);
	}
}
