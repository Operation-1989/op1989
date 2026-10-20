using UnityEngine;
using UnityEngine.InputSystem;

public class TestPlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;

    void Update()
    {
        if (Keyboard.current == null)
            return;

        Vector3 movement = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
            movement.z += 1f;

        if (Keyboard.current.sKey.isPressed)
            movement.z -= 1f;

        if (Keyboard.current.aKey.isPressed)
            movement.x -= 1f;

        if (Keyboard.current.dKey.isPressed)
            movement.x += 1f;

        movement = movement.normalized;

        transform.position += movement * moveSpeed * Time.deltaTime;
    }
}