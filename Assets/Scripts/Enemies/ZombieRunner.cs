using UnityEngine;
using UnityEngine.AI;

public class ZombieRunner : Enemy
{
    protected override void Awake()
    {
        enemyName = "Zombie Runner";
        maxHealth = 30;
        attackDamage = 8;
        attackRate = 0.8f;
        moveSpeed = 5f;
        detectionRange = 25f;
        attackRange = 1.8f;
        base.Awake();
    }

    protected override void Start()
    {
        base.Start();
        Renderer rend = GetComponent<Renderer>();
        if (rend != null) rend.material.color = Color.green;
    }

    protected override void HandleBehavior(float distanceToPlayer)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (player == null) return;

        if (distanceToPlayer <= attackRange)
        {
            Attack();
            agent.SetDestination(transform.position);
        }
        else
        {
            agent.SetDestination(player.position);
        }
    }
}
