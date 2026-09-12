using UnityEngine;

public class Shotgun : Weapon
{
    public int pelletCount = 8;
    public float spreadAngle = 5f;
    public int pelletDamage = 8;
    private WeaponManager weaponManagerRef;

    protected override void Awake()
    {
        weaponName = "Shotgun";
        soundFolder = "Shotgun";
        soundBaseName = "shotgun";
        damage = 64;
        fireRate = 0.8f;
        range = 30f;
        usesAmmo = true;
        maxAmmo = 50;
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
                if (enemy == null) enemy = hit.collider.GetComponentInParent<Enemy>();
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
