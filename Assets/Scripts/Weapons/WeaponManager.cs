using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    public Weapon[] weapons;
    public int currentWeaponIndex = 0;
    private PlayerHUD hud;

    void Start()
    {
        hud = FindObjectOfType<PlayerHUD>();
        InitializeWeapons();
        EquipWeapon(0);
    }

    void InitializeWeapons()
    {
        Camera cam = GetComponent<PlayerController>()?.playerCamera;
        if (cam == null)
        {
            PlayerController pc = FindObjectOfType<PlayerController>();
            if (pc != null) cam = pc.playerCamera;
        }

        weapons = new Weapon[4];
        weapons[0] = gameObject.AddComponent<Chainsaw>();
        weapons[1] = gameObject.AddComponent<Pistol>();
        weapons[2] = gameObject.AddComponent<Shotgun>();
        weapons[3] = gameObject.AddComponent<AssaultRifle>();

        foreach (Weapon w in weapons)
        {
            w.enabled = false;
            w.Initialize(cam, this);
        }
    }

    void Update()
    {
        if (FindObjectOfType<PlayerController>()?.IsDead() == true) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) EquipWeapon(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) EquipWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) EquipWeapon(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) EquipWeapon(3);

        if (Input.GetKeyDown(KeyCode.R))
        {
            Weapon current = weapons[currentWeaponIndex];
            if (current != null && current.UsesAmmo())
            {
                current.StartReload();
            }
        }
    }

    public void EquipWeapon(int index)
    {
        if (index < 0 || index >= weapons.Length) return;

        if (weapons[currentWeaponIndex] != null)
            weapons[currentWeaponIndex].enabled = false;

        currentWeaponIndex = index;

        if (weapons[currentWeaponIndex] != null)
            weapons[currentWeaponIndex].enabled = true;

        UpdateHUD();
    }

    public void UpdateHUD()
    {
        if (hud != null && weapons[currentWeaponIndex] != null)
        {
            Weapon w = weapons[currentWeaponIndex];
            hud.UpdateWeapon(w.GetName(), w.GetAmmo(), !w.UsesAmmo());
        }
    }

    public Weapon GetCurrentWeapon()
    {
        return weapons[currentWeaponIndex];
    }

    public Weapon[] GetWeapons() => weapons;
}
