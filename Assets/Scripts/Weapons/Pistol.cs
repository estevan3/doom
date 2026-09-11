using UnityEngine;

public class Pistol : Weapon
{
    void Awake()
    {
        weaponName = "Pistol";
        damage = 15;
        fireRate = 0.3f;
        range = 100f;
        usesAmmo = true;
        maxAmmo = 999;
        reloadTime = 0.5f;
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

        Debug.DrawRay(ray.origin, ray.direction * range, Color.green, 0.1f);

        if (Physics.Raycast(ray, out hit, range))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
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
