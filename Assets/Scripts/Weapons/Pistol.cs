using UnityEngine;

public class Pistol : Weapon
{
    private WeaponManager weaponManagerRef;

    protected override void Awake()
    {
        weaponName = "Pistol";
        damage = 15;
        fireRate = 0.3f;
        range = 100f;
        usesAmmo = true;
        maxAmmo = 999;
        reloadTime = 0.5f;
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
    }

    protected override void PerformAttack()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, range))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
            if (enemy == null) enemy = hit.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                CreateImpactEffect(hit.point, Color.red);
            }
            else
            {
                CreateImpactEffect(hit.point, Color.white);
            }
        }
    }
}
