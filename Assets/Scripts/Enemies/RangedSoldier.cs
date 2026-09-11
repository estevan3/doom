using UnityEngine;
using UnityEngine.AI;

public class RangedSoldier : Enemy
{
    public float preferredDistance = 12f;
    public float strafeSpeed = 2f;
    public float bulletSpeed = 20f;
    private Vector3 strafeDirection;
    private float nextStrafeChange = 0f;

    void Awake()
    {
        enemyName = "Ranged Soldier";
        maxHealth = 60;
        attackDamage = 12;
        attackRate = 1.5f;
        moveSpeed = 3.5f;
        detectionRange = 30f;
        attackRange = 25f;
    }

    protected override void Start()
    {
        base.Start();
        Renderer rend = GetComponent<Renderer>();
        if (rend != null) rend.material.color = new Color(1f, 0.6f, 0f);
        strafeDirection = transform.right;
    }

    protected override void HandleBehavior(float distanceToPlayer)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (player == null) return;

        Vector3 directionToPlayer = (player.position - transform.position).normalized;

        transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));

        if (distanceToPlayer < preferredDistance - 2f)
        {
            agent.SetDestination(transform.position - directionToPlayer * 3f);
        }
        else if (distanceToPlayer > preferredDistance + 2f)
        {
            agent.SetDestination(player.position);
        }
        else
        {
            if (Time.time > nextStrafeChange)
            {
                strafeDirection = Random.value > 0.5f ? transform.right : -transform.right;
                nextStrafeChange = Time.time + 1.5f;
            }
            agent.SetDestination(transform.position + strafeDirection * strafeSpeed);
        }

        if (distanceToPlayer <= attackRange)
        {
            Attack();
        }
    }

    protected override void Attack()
    {
        if (player == null) return;
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackRate;

        Vector3 direction = (player.position + Vector3.up * 1f - transform.position).normalized;

        Debug.DrawRay(transform.position + Vector3.up, direction * attackRange, Color.orange, 0.2f);

        Ray ray = new Ray(transform.position + Vector3.up, direction);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, attackRange))
        {
            PlayerController playerController = hit.collider.GetComponent<PlayerController>();
            if (playerController == null)
            {
                playerController = hit.collider.GetComponentInParent<PlayerController>();
            }
            if (playerController != null)
            {
                playerController.TakeDamage(attackDamage);
                CreateImpactEffect(hit.point, Color.orange);
            }
        }
    }
}
