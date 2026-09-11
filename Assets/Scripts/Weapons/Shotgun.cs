using UnityEngine;

public class Shotgun : Weapon
{
    public int pelletCount = 8;
    public float spreadAngle = 5f;
    public int pelletDamage = 8;

    void Awake()
    {
        weaponName = "Shotgun";
        damage = 64;
        fireRate = 0.8f;
        range = 30f;
        usesAmmo = true;
        maxAmmo = 50;
        reloadTime = 2.0f;
    }

    protected override void PerformAttack()
    {
        for (int i = 0; i < pelletCount; i++)
        {
            Vector3 spread = playerCamera.transform.forward;
            spread += playerCamera.transform.right * Random.Range(-spreadAngle, spreadAngle) * 0.01f;
            spread += playerCamera.transform.up * Random.Range(-spreadAngle, spreadAngle) * 0.01f;

            Ray ray = new Ray(playerCamera.transform.position, spread);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, range))
            {
                Enemy enemy = hit.collider.GetComponent<Enemy>();
                if (enemy != null)
                {
                    enemy.TakeDamage(pelletDamage);
                    CreateImpactEffect(hit.point, new Color(1f, 0.5f, 0f));
                }
                else
                {
                    CreateImpactEffect(hit.point, Color.gray);
                }
            }
        }
    }
}
