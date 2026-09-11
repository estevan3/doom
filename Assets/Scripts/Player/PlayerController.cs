using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 10f;
    public float jumpForce = 8f;
    public float gravity = 20f;

    [Header("Look")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;

    [Header("Health")]
    public int maxHealth = 100;
    public int currentHealth;

    [Header("References")]
    public Camera playerCamera;
    public CharacterController characterController;

    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;
    private bool isSprinting = false;
    private bool isDead = false;

    public delegate void PlayerDeathHandler();
    public event PlayerDeathHandler OnPlayerDeath;

    public delegate void HealthChangedHandler(int current, int max);
    public event HealthChangedHandler OnHealthChanged;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        if (playerCamera == null)
        {
            GameObject camObj = new GameObject("PlayerCamera");
            camObj.transform.SetParent(transform);
            camObj.transform.localPosition = new Vector3(0, 0.8f, 0);
            playerCamera = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }

        currentHealth = maxHealth;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (isDead) return;

        HandleMovement();
        HandleLook();
        HandleJump();
    }

    void HandleMovement()
    {
        isSprinting = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        moveDirection.x = (forward * vertical + right * horizontal).x * currentSpeed;
        moveDirection.z = (forward * vertical + right * horizontal).z * currentSpeed;

        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        characterController.Move(moveDirection * Time.deltaTime);
    }

    void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);

        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        transform.Rotate(0, mouseX, 0);
    }

    void HandleJump()
    {
        if (characterController.isGrounded && Input.GetButtonDown("Jump"))
        {
            moveDirection.y = jumpForce;
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    void Die()
    {
        isDead = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        OnPlayerDeath?.Invoke();
    }

    public bool IsDead() => isDead;
    public float GetHealthPercent() => (float)currentHealth / maxHealth;
}
