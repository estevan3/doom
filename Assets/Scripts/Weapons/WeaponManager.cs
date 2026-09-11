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

        var input = PlayerInputActions.Instance;
        if (input == null) return;

        if (input.weapon1Action.WasPressedThisFrame()) EquipWeapon(0);
        if (input.weapon2Action.WasPressedThisFrame()) EquipWeapon(1);
        if (input.weapon3Action.WasPressedThisFrame()) EquipWeapon(2);
        if (input.weapon4Action.WasPressedThisFrame()) EquipWeapon(3);

        if (input.reloadAction.WasPressedThisFrame())
        {
            Weapon current = weapons[currentWeaponIndex];
            if (current != null && current.UsesAmmo())
            {
                current.StartReload();
            }
        }
    }

    public bool IsFirePressed()
    {
        var input = PlayerInputActions.Instance;
        return input != null && input.fireAction.IsPressed();
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
