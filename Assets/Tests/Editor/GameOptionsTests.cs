using NUnit.Framework;
using UnityEngine;

public class GameOptionsTests
{
    private const string VhsKey = "opt_vhs_activo";
    private const string LegacyKey = "opt_vhs_menus";
    private bool _hadOriginal, _hadLegacy;
    private int _original, _legacy;

    [SetUp]
    public void SaveOriginalPreference()
    {
        _hadOriginal = PlayerPrefs.HasKey(VhsKey);
        _original = PlayerPrefs.GetInt(VhsKey, 1);
        _hadLegacy = PlayerPrefs.HasKey(LegacyKey);
        _legacy = PlayerPrefs.GetInt(LegacyKey, 1);
    }

    [TearDown]
    public void RestoreOriginalPreference()
    {
        if (_hadOriginal) PlayerPrefs.SetInt(VhsKey, _original);
        else PlayerPrefs.DeleteKey(VhsKey);
        if (_hadLegacy) PlayerPrefs.SetInt(LegacyKey, _legacy);
        else PlayerPrefs.DeleteKey(LegacyKey);
        PlayerPrefs.Save();
    }

    [Test]
    public void VhsPreferencePersistsWhenDisabled()
    {
        GameOptions.VhsActivo = false;

        Assert.That(PlayerPrefs.GetInt(VhsKey, 1), Is.EqualTo(0));
        Assert.That(GameOptions.VhsActivo, Is.False);
        Assert.That(Gameplay.VHSSettings.Activo, Is.False);
        Assert.That(Gameplay.VHSSettings.AmtGrano, Is.EqualTo(0f));
    }

    [Test]
    public void VhsPreferencePersistsWhenEnabledAgain()
    {
        GameOptions.VhsActivo = false;
        GameOptions.VhsActivo = true;

        Assert.That(PlayerPrefs.GetInt(VhsKey, 0), Is.EqualTo(1));
        Assert.That(GameOptions.VhsActivo, Is.True);
    }

    [Test]
    public void LegacyDisabledPreferenceDoesNotReactivateVhs()
    {
        PlayerPrefs.DeleteKey(VhsKey);
        PlayerPrefs.SetInt(LegacyKey, 0);
        PlayerPrefs.Save();

        Assert.That(GameOptions.VhsActivo, Is.False);
    }

    [Test]
    public void DisabledVhsSkipsWholeCameraPassEvenAtMaximumTension()
    {
        Assert.That(Gameplay.CameraFXOverlay.ShouldRender(
            enPartida: true, vhsActivo: false, vhsTieneIngredientes: true,
            tension: 1f, minVisible: 0.02f), Is.False);
    }
}
