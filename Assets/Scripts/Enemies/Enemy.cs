using UnityEngine;
using UnityEngine.AI;

public abstract class Enemy : MonoBehaviour
{
    [Header("Enemy Stats")]
    public string enemyName = "Enemy";
    public int maxHealth = 50;
    public int currentHealth;
    public int attackDamage = 10;
    public float attackRate = 1f;
    public float moveSpeed = 3.5f;
    public float detectionRange = 20f;
    public float attackRange = 2f;

    [Header("References")]
    public Transform player;
    protected NavMeshAgent agent;
    protected float nextAttackTime = 0f;
    protected bool isDead = false;

    public delegate void EnemyDeathHandler(Enemy enemy);
    public event EnemyDeathHandler OnEnemyDeath;

    public delegate void EnemyDamagedHandler(Enemy enemy, int damage);
    public event EnemyDamagedHandler OnEnemyDamaged;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }
        agent.speed = moveSpeed;
    }

    protected virtual void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            PlayerController pc = FindObjectOfType<PlayerController>();
            if (pc != null) player = pc.transform;
        }
    }

    protected virtual void Update()
    {
        if (isDead || player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= detectionRange)
        {
            HandleBehavior(distanceToPlayer);
        }
    }

    protected abstract void HandleBehavior(float distanceToPlayer);

    public virtual void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        OnEnemyDamaged?.Invoke(this, damage);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    protected virtual void Die()
    {
        isDead = true;
        OnEnemyDeath?.Invoke(this);

        if (agent != null) agent.enabled = false;

        StartCoroutine(DeathSequence());
    }

    protected virtual System.Collections.IEnumerator DeathSequence()
    {
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = Color.black;
        }

        yield return new WaitForSeconds(0.5f);

        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.zero, Time.deltaTime * 5f);
        yield return new WaitForSeconds(0.3f);

        Destroy(gameObject);
    }

    protected virtual void Attack()
    {
        if (Time.time < nextAttackTime) return;

        nextAttackTime = Time.time + attackRate;

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.TakeDamage(attackDamage);
        }
    }

    public bool IsDead() => isDead;
    public int GetHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
    public string GetName() => enemyName;

    protected void CreateImpactEffect(Vector3 position, Color color)
    {
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        impact.transform.position = position;
        impact.transform.localScale = Vector3.one * 0.15f;
        Renderer rend = impact.GetComponent<Renderer>();
        rend.material.color = color;
        Destroy(impact, 0.1f);
    }
}
