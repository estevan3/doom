using UnityEngine;
using UnityEngine.AI;

public class ZombieRunner : Enemy
{
    void Awake()
    {
        enemyName = "Zombie Runner";
        maxHealth = 30;
        attackDamage = 8;
        attackRate = 0.8f;
        moveSpeed = 5f;
        detectionRange = 25f;
        attackRange = 1.8f;
    }

    protected override void Start()
    {
        base.Start();
        GetComponent<Renderer>().material.color = Color.green;
    }

    protected override void HandleBehavior(float distanceToPlayer)
    {
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
