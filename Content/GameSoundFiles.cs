using OpenKogama.Api;

namespace OpenKogama.Content;

public static class GameSoundFiles
{
    const string Root = "AssetBundles/Sounds/";
    const string Extension = ".unity3d";

    static readonly Dictionary<GameSound, string> Files = new()
    {
        [GameSound.GunAk47] = "guns/ak47",
        [GameSound.GunBazooka] = "guns/bazooka",
        [GameSound.GunBlaster] = "guns/blaster",
        [GameSound.GunBlasterBurst] = "guns/blaster_burst",
        [GameSound.GunBlasterHeavy] = "guns/blaster_heavy",
        [GameSound.GunCanon] = "guns/canon",
        [GameSound.GunCanonAir] = "guns/canon_air",
        [GameSound.GunCanonPotato] = "guns/canon_potato",
        [GameSound.GunCubeGunFire] = "guns/cubegun_fire",
        [GameSound.GunFlamethrower] = "guns/flamethrower",
        [GameSound.GunShot] = "guns/gun_shot",
        [GameSound.GunHandgun] = "guns/handgun",
        [GameSound.GunHandgunSmall] = "guns/handgun_small",
        [GameSound.GunLightGun] = "guns/light_gun",
        [GameSound.GunM1Shot] = "guns/m1_shot",
        [GameSound.GunMachineGun] = "guns/machine_gun",
        [GameSound.GunMachineGunHeavy] = "guns/machine_gun_heavy",
        [GameSound.GunMinigun] = "guns/minigun",
        [GameSound.GunMusket] = "guns/musket",
        [GameSound.GunPlasmaCanon] = "guns/plasma_canon",
        [GameSound.GunRailgun] = "guns/railgun",
        [GameSound.GunRevolver] = "guns/revolver",
        [GameSound.GunShotgun] = "guns/shotgun",
        [GameSound.GunSilencedMachineGun] = "guns/silenced_machinegun",
        [GameSound.GunSilencedPistol] = "guns/silenced_pistol",
        [GameSound.GunSizeGun] = "guns/sizegun",
        [GameSound.GunSmg] = "guns/smg",
        [GameSound.GunSmgHeavy] = "guns/smg_heavy",
        [GameSound.GunSmgLight] = "guns/smg_light",
        [GameSound.GunSmgSilenced] = "guns/smg_silenced",
        [GameSound.GunSmgSmall] = "guns/smg_small",
        [GameSound.GunSniper] = "guns/sniper",
        [GameSound.GunSpellBurst] = "guns/spell_burst",
        [GameSound.GunSpellFlare] = "guns/spell_flare",
        [GameSound.GunSpellRocket] = "guns/spell_rocket",
        [GameSound.GunSpellWave] = "guns/spell_wave",
        [GameSound.GunSpinningProjectile] = "guns/spinning_projectile",

        [GameSound.HitBulletOpponent] = "hit/bullet_hitopponent",
        [GameSound.HitBulletSurface] = "hit/bullet_hitsurface",
        [GameSound.HitClink] = "hit/clink",
        [GameSound.HitCube] = "hit/hitcube2",
        [GameSound.HitExplosion] = "hit/explosion",
        [GameSound.HitGrass] = "hit/grass",
        [GameSound.HitGrassBlade] = "hit/grass_blade",
        [GameSound.HitGrassLight] = "hit/grass_light",
        [GameSound.HitGrassScythe] = "hit/grass_scythe",
        [GameSound.HitMetalClash] = "hit/metalclash",
        [GameSound.HitMetallicBang] = "hit/metalic_bang",
        [GameSound.HitMetallicBoom] = "hit/metalic_boom",
        [GameSound.HitMetallicClonk] = "hit/metalic_clonk",
        [GameSound.HitPop] = "hit/pop",
        [GameSound.HitSciFi] = "hit/scifihit",
        [GameSound.HitWesternRicochet] = "hit/western ricochet",
        [GameSound.HitWoodenBang] = "hit/wooden_bang",

        [GameSound.MeleeBonk] = "melee/bonk",
        [GameSound.MeleePunch] = "melee/punch",
        [GameSound.MeleeSciFiSwing] = "melee/scifiswing",
        [GameSound.MeleeSlap] = "melee/slap",
        [GameSound.MeleeSlash] = "melee/slash",
        [GameSound.MeleeSwordSwing] = "melee/swordswing",
        [GameSound.MeleeThud] = "melee/thud",

        [GameSound.PickupCostume] = "pickup/pickupcostume",
        [GameSound.PickupGem] = "pickup/pickup_gem_1",
        [GameSound.PickupGun] = "pickup/pickup_gun",
        [GameSound.PickupHealth] = "pickup/pickup_health",
        [GameSound.PickupMagic] = "pickup/magicsound",
        [GameSound.PickupVehicleEnergy] = "pickup/pickup_vehicle-energy",

        [GameSound.LoopAdvGhost] = "loops/advghost00",
        [GameSound.LoopBacterio] = "loops/bacterio-01",
        [GameSound.LoopBoiling] = "loops/boiling",
        [GameSound.LoopChargingBuzzer] = "loops/charging_buzzer",
        [GameSound.LoopChargingClock] = "loops/charging_clock",
        [GameSound.LoopChargingHum] = "loops/charging_hum",
        [GameSound.LoopChargingScrewdriver] = "loops/charging_screwdriver",
        [GameSound.LoopEthereal] = "loops/etherial",
        [GameSound.LoopImpulse] = "loops/impulse",
        [GameSound.LoopJet] = "loops/jet",
        [GameSound.LoopJetpackLarge] = "loops/jetpack_large",
        [GameSound.LoopJetpackSmall] = "loops/jetpack_small",
        [GameSound.LoopPushbikeSkids] = "loops/jfo_pushbike_wheel_skids",
        [GameSound.LoopRollingBall] = "loops/rolling_ball_loop04",
        [GameSound.LoopSciFi] = "loops/sci-fi",
        [GameSound.LoopSnowSentry] = "loops/snowsentry",
        [GameSound.LoopThrowingStar] = "loops/throwingstarinair",

        [GameSound.BuildCubeAdded] = "build/build_cubeadded",
        [GameSound.BuildFaceMoved] = "build/build_facemoved",
        [GameSound.BuildTranslateGrid] = "build/translate_grid",
        [GameSound.BuildTranslateNoGrid] = "build/translate_notgrid",

        [GameSound.GuiButtonClick] = "Gui/buttonclick",
        [GameSound.GuiButtonClick3] = "Gui/buttonclick3",
        [GameSound.GuiPurchase] = "Gui/purchasesound",
        [GameSound.GuiScreenshot] = "Gui/screenshot",
        [GameSound.GuiSqueak13] = "Gui/kgm_gui_squeak13",
        [GameSound.GuiWoodB2] = "Gui/kgm_gui_woodb2",
        [GameSound.GuiWoodC3] = "Gui/kgm_gui_woodc3",
        [GameSound.GuiWoodC10] = "Gui/kgm_gui_woodc10",

        [GameSound.StepBrickWall] = "Avatar/kgm_ava_brickwall_walk",
        [GameSound.StepPavement] = "Avatar/kgm_ava_pavement_walk",

        [GameSound.CharacterSkeletonDie] = "character/skeleton_die_01",
        [GameSound.CharacterSkeletonPain] = "character/skeleton_pain_01",

        [GameSound.MiscCashRegister] = "misc/cashregister",
        [GameSound.MiscFirstTimePopup] = "misc/firsttimepopup",
        [GameSound.MiscNotification] = "misc/notification",
        [GameSound.MiscSizeGrow] = "misc/sizegrow",
        [GameSound.MiscSizeShrink] = "misc/sizeshrink",
        [GameSound.MiscTadaa] = "misc/tadaa",
        [GameSound.MiscThrow] = "misc/throw",
        [GameSound.MiscThrow2] = "misc/throw2",
        [GameSound.MiscThrow3] = "misc/throw3",
        [GameSound.MiscWoosh] = "misc/woosh",

        [GameSound.OldExplosives] = "old/explosives",
        [GameSound.OldFireLoop] = "old/fire_loop",
        [GameSound.OldLaserShot] = "old/laser_single_shot",
        [GameSound.OldPickup] = "old/pickup",
        [GameSound.OldPickupHealth] = "old/pickup_health",
    };

    static readonly Dictionary<string, GameSound> ByPath =
        Files.ToDictionary(pair => Root + pair.Value + Extension, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static GameSound? Find(string file) => ByPath.TryGetValue(file, out GameSound sound) ? sound : null;

    public static string CacheName(GameSound sound) => Path.GetFileName(Files[sound]);
}
