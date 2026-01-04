
using UnityEngine;

/// <summary>
/// Simple component to make debris slowly tumble in space.
/// </summary>
public class DebrisTumble : MonoBehaviour
{
	/// <summary>
	/// Rotation speed in degrees per second for each axis.
	/// </summary>
	public Vector3 m_rotationSpeed = new Vector3( 5f, 5f, 5f );

	void Update()
	{
		transform.Rotate( m_rotationSpeed * Time.deltaTime );
	}
}
