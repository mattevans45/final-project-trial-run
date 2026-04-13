/// <summary>
/// Contract for all weapons mountable on a vehicle.
///
/// Separating the interface from the implementation lets you swap weapon
/// types (pistol → shotgun → machine gun) from PlayerCar without touching
/// any weapon-specific code, and makes weapon logic testable without
/// requiring a real input device.
/// </summary>
public interface IWeapon
{
    /// <summary>Rounds currently loaded in the weapon.</summary>
    int  CurrentAmmo { get; }

    /// <summary>Maximum clip capacity for this weapon.</summary>
    int  MaxAmmo     { get; }

    /// <summary>True while a reload cycle is in progress.</summary>
    bool IsReloading { get; }

    /// <summary>
    /// Attempt to fire one round. Silently no-ops when out of ammo or reloading.
    /// The caller (PlayerCar) is responsible for deciding WHEN to call this —
    /// the weapon itself no longer polls input.
    /// </summary>
    void TryFire();

    /// <summary>
    /// Begin a manual reload cycle. No-op if already reloading or at full ammo.
    /// </summary>
    void StartManualReload();
}
