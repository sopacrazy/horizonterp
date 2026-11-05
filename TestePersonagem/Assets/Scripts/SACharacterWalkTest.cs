using UnityEngine;

// Small offline fixture for reviewing the two new locomotion clips.
[RequireComponent(typeof(CharacterController))]
public sealed class SACharacterWalkTest : MonoBehaviour
{
    public Animator animator;
    public Camera followCamera;
    public float walkSpeed = 1.3756f;
    CharacterController body;
    float vertical;

    void Awake() { body = GetComponent<CharacterController>(); }
    void Update()
    {
        var input = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1);
        var direction = new Vector3(input.x, 0, input.y);
        if (direction.sqrMagnitude > .001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540 * Time.deltaTime);
        if (body.isGrounded && vertical < 0) vertical = -2;
        vertical = Mathf.Max(-30, vertical - 22 * Time.deltaTime);
        body.Move((direction * walkSpeed + Vector3.up * vertical) * Time.deltaTime);
        animator.SetFloat("Speed", direction.magnitude * walkSpeed, .12f, Time.deltaTime);
    }
    void LateUpdate()
    {
        if (!followCamera) return;
        followCamera.transform.position = transform.position + new Vector3(0, 1.65f, -3.7f);
        followCamera.transform.LookAt(transform.position + Vector3.up * 1.05f);
    }
    void OnGUI()
    {
        GUI.Box(new Rect(16, 16, 350, 54), "Teste do personagem SA\nWASD ou setas: andar | Soltar: ficar parado");
    }
}
