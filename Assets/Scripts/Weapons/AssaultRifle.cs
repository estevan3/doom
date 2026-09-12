using UnityEngine;

public class AssaultRifle : Weapon
{
    private WeaponManager weaponManagerRef;

    protected override void Awake()
    {
        weaponName = "Assault Rifle";
        soundFolder = "AssaultRifle";
        soundBaseName = "assault_rifle";
        damage = 10;
        fireRate = 0.1f;
        range = 150f;
        usesAmmo = true;
        maxAmmo = 120;
        reloadTime = 2.0f;
        base.Awake();
    }

    public override void Initialize(Camera cam, WeaponManager manager)
    {
        base.Initialize(cam, manager);
        weaponManagerRef = manager;
    }

    public override void Update()
    {
        if (weaponManagerRef != null && weaponManagerRef.IsFirePressed() && CanFire())
        {
            Fire();
        }
    }

    public override bool CanFire()
    {
        if (Time.time < nextFireTime) return false;
        if (isReloading) return false;
        if (usesAmmo && currentAmmo <= 0) return false;
        return true;
    }

    public override void Fire()
    {
        if (!CanFire()) return;

        nextFireTime = Time.time + fireRate;

        if (usesAmmo)
        {
            currentAmmo--;
            InvokeAmmoChanged(currentAmmo, false);
        }

        PerformAttack();
        PlayFireSound();
    }

    protected override void PerformAttack()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        Vector3 spread = ray.direction;
        spread += playerCamera.transform.right * Random.Range(-0.02f, 0.02f);
        spread += playerCamera.transform.up * Random.Range(-0.02f, 0.02f);
        ray.direction = spread.normalized;

        if (Physics.Raycast(ray, out hit, range))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
            if (enemy == null) enemy = hit.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                CreateImpactEffect(hit.point, new Color(1f, 0.8f, 0f));
            }
            else
            {
                CreateImpactEffect(hit.point, Color.white);
            }
        }
    }
}
