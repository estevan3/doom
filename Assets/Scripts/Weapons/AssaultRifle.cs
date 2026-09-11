using UnityEngine;

public class AssaultRifle : Weapon
{
    void Awake()
    {
        weaponName = "Assault Rifle";
        damage = 10;
        fireRate = 0.1f;
        range = 150f;
        usesAmmo = true;
        maxAmmo = 120;
        reloadTime = 2.0f;
    }

    void Update()
    {
        if (Input.GetMouseButton(0) && CanFire())
        {
            Fire();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            StartReload();
        }
    }

    protected override void PerformAttack()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        Vector3 spread = ray.direction;
        spread += playerCamera.transform.right * Random.Range(-0.02f, 0.02f);
        spread += playerCamera.transform.up * Random.Range(-0.02f, 0.02f);
        ray.direction = spread.normalized;

        Debug.DrawRay(ray.origin, ray.direction * range, Color.red, 0.1f);

        if (Physics.Raycast(ray, out hit, range))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
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
