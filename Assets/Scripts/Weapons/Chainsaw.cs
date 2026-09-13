using UnityEngine;

public class Chainsaw : Weapon
{
    public float damagePerSecond = 30f;
    public float attackRange = 3f;
    private bool isAttacking = false;
    private CameraShake cameraShake;

    protected override void Awake()
    {
        weaponName = "Chainsaw";
        damage = 30;
        fireRate = 0.1f;
        usesAmmo = false;
        maxAmmo = 0;
        base.Awake();
    }

    public override void Initialize(Camera cam, WeaponManager manager)
    {
        base.Initialize(cam, manager);
        weaponManager = manager;
        cameraShake = cam.GetComponent<CameraShake>();
    }

    public override void Update()
    {
        if (weaponManager != null && !weaponManager.IsPlayerDead() && weaponManager.IsFirePressed() && CanFire())
        {
            isAttacking = true;
            Fire();
        }
        else
        {
            isAttacking = false;
        }

        if (isAttacking && cameraShake != null)
        {
            cameraShake.Shake(0.02f, 0.05f);
        }
    }

    public override bool CanFire()
    {
        if (Time.time < nextFireTime) return false;
        return !isReloading;
    }

    public override void Fire()
    {
        if (!CanFire()) return;
        nextFireTime = Time.time + fireRate;
        PerformAttack();
        PlayFireSound();
    }

    protected override void PerformAttack()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, attackRange))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
            if (enemy == null) enemy = hit.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                CreateImpactEffect(hit.point, Color.yellow);
            }
        }
    }
}
