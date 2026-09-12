using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    [Header("Weapon Stats")]
    public string weaponName = "Weapon";
    public int damage = 10;
    public float fireRate = 0.5f;
    public float range = 100f;
    public bool usesAmmo = true;
    public int maxAmmo = 50;
    public int currentAmmo;
    public float reloadTime = 1.5f;

    [Header("References")]
    public Camera playerCamera;
    public Transform muzzlePoint;

    [Header("Audio")]
    public AudioClip[] fireSounds = new AudioClip[3];
    [SerializeField] private AudioClip equipSound;

    protected float nextFireTime = 0f;
    protected bool isReloading = false;
    protected WeaponManager weaponManager;
    protected AudioSource audioSource;
    protected string soundFolder = "";
    protected string soundBaseName = "";

    public delegate void AmmoChangedHandler(int current, bool infinite);
    public event AmmoChangedHandler OnAmmoChanged;

    protected virtual void Awake()
    {
        currentAmmo = maxAmmo;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        LoadWeaponAudio();
    }

    protected virtual void LoadWeaponAudio()
    {
        if (string.IsNullOrEmpty(soundFolder) || string.IsNullOrEmpty(soundBaseName)) return;
        equipSound = Resources.Load<AudioClip>($"WeaponSounds/{soundFolder}/{soundBaseName}_weapon_equip");
        for (int i = 0; i < fireSounds.Length; i++)
            fireSounds[i] = Resources.Load<AudioClip>($"WeaponSounds/{soundFolder}/{soundBaseName}_gunshot_0{i + 1}");
    }

    public virtual void PlayEquipSound()
    {
        if (audioSource == null || equipSound == null) return;
        audioSource.PlayOneShot(equipSound);
    }

    public virtual void PlayFireSound()
    {
        if (audioSource == null || fireSounds == null || fireSounds.Length == 0) return;
        AudioClip clip = fireSounds[Random.Range(0, fireSounds.Length)];
        if (clip != null) audioSource.PlayOneShot(clip);
    }

    public virtual void Initialize(Camera cam, WeaponManager manager)
    {
        playerCamera = cam;
        weaponManager = manager;
    }

    public virtual void Update()
    {
        if (isReloading) return;
    }

    public virtual bool CanFire()
    {
        if (Time.time < nextFireTime) return false;
        if (isReloading) return false;
        if (usesAmmo && currentAmmo <= 0) return false;
        return true;
    }

    public virtual void Fire()
    {
        if (!CanFire()) return;

        nextFireTime = Time.time + fireRate;

        if (usesAmmo)
        {
            currentAmmo--;
            OnAmmoChanged?.Invoke(currentAmmo, false);
        }

        PerformAttack();
    }

    protected abstract void PerformAttack();

    public virtual void StartReload()
    {
        if (isReloading || !usesAmmo) return;
        if (currentAmmo >= maxAmmo) return;
        isReloading = true;
        Invoke(nameof(FinishReload), reloadTime);
    }

    protected virtual void FinishReload()
    {
        currentAmmo = maxAmmo;
        isReloading = false;
        OnAmmoChanged?.Invoke(currentAmmo, false);
    }

    public virtual void AddAmmo(int amount)
    {
        currentAmmo = Mathf.Min(currentAmmo + amount, maxAmmo);
        OnAmmoChanged?.Invoke(currentAmmo, false);
    }

    public bool IsReloading() => isReloading;
    public string GetName() => weaponName;
    public int GetAmmo() => currentAmmo;
    public bool UsesAmmo() => usesAmmo;

    protected void CreateImpactEffect(Vector3 position, Color color)
    {
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        impact.transform.position = position;
        impact.transform.localScale = Vector3.one * 0.15f;
        Renderer rend = impact.GetComponent<Renderer>();
        rend.material.color = color;
        Destroy(impact, 0.1f);
    }

    protected void InvokeAmmoChanged(int current, bool infinite)
    {
        OnAmmoChanged?.Invoke(current, infinite);
    }
}
