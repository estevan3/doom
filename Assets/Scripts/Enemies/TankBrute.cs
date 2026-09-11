using UnityEngine;
using UnityEngine.AI;

public class TankBrute : Enemy
{
    public float telegraphDuration = 0.5f;
    public int slamDamage = 30;
    public float slamRange = 3f;
    private bool isTelegraphing = false;
    private float telegraphTimer = 0f;
    private Renderer enemyRenderer;
    private Color originalColor;

    void Awake()
    {
        enemyName = "Tank Brute";
        maxHealth = 200;
        attackDamage = 25;
        attackRate = 2.5f;
        moveSpeed = 1.5f;
        detectionRange = 20f;
        attackRange = 3f;
    }

    protected override void Start()
    {
        base.Start();
        enemyRenderer = GetComponent<Renderer>();
        if (enemyRenderer != null)
        {
            originalColor = new Color(0.6f, 0f, 0f);
            enemyRenderer.material.color = originalColor;
        }
        transform.localScale = Vector3.one * 2f;
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.height = 4f;
            agent.radius = 1.5f;
        }
    }

    protected override void HandleBehavior(float distanceToPlayer)
    {
        Vector3 directionToPlayer = (player.position - transform.position).normalized;

        transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));

        if (distanceToPlayer <= attackRange && !isTelegraphing)
        {
            StartTelegraph();
        }
        else if (isTelegraphing)
        {
            telegraphTimer -= Time.deltaTime;
            float flashIntensity = Mathf.PingPong(Time.time * 8f, 1f);
            if (enemyRenderer != null)
            {
                enemyRenderer.material.color = Color.Lerp(originalColor, Color.yellow, flashIntensity);
            }

            if (telegraphTimer <= 0)
            {
                FinishTelegraph();
            }
        }
        else
        {
            agent.SetDestination(player.position);
        }
    }

    void StartTelegraph()
    {
        isTelegraphing = true;
        telegraphTimer = telegraphDuration;
        agent.SetDestination(transform.position);
    }

    void FinishTelegraph()
    {
        isTelegraphing = false;

        if (enemyRenderer != null)
        {
            enemyRenderer.material.color = originalColor;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * 1.5f, slamRange);

        foreach (Collider hit in hits)
        {
            PlayerController playerController = hit.GetComponent<PlayerController>();
            if (playerController == null)
            {
                playerController = hit.GetComponentInParent<PlayerController>();
            }
            if (playerController != null)
            {
                playerController.TakeDamage(slamDamage);
                break;
            }
        }

        Debug.DrawRay(transform.position + Vector3.up, transform.forward * slamRange, Color.red, 0.3f);
        CreateSlamEffect();
    }

    void CreateSlamEffect()
    {
        GameObject slamEffect = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        slamEffect.transform.position = transform.position + transform.forward * 1.5f;
        slamEffect.transform.localScale = Vector3.one * slamRange * 2f;
        Renderer rend = slamEffect.GetComponent<Renderer>();
        rend.material.color = new Color(1f, 0.3f, 0f, 0.5f);

        Collider col = slamEffect.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Destroy(slamEffect, 0.3f);
    }
}
