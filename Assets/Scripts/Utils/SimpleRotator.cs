using UnityEngine;

namespace Starflight.Utils
{
    public class SimpleRotator : MonoBehaviour
    {
        [SerializeField] private Vector3 _axis = Vector3.up;
        [SerializeField] private float _speed = 10f;

        private void Update()
        {
            transform.Rotate(_axis, _speed * Time.deltaTime);
        }
    }
}
