using Gameplay;
using NUnit.Framework;
using static Gameplay.DeviceCompatibility;

namespace Mortuorium.Tests.EditMode
{
    // Cubre el parseo puro de DeviceCompatibility (US-1.1: Android < 14 / iOS < 17 no
    // son aceptados). No depende de SystemInfo ni de un dispositivo real: los strings
    // de SystemInfo.operatingSystem se pasan como parámetro para poder cubrir los
    // casos borde (umbral exacto, formatos inesperados) en el Editor.
    public class DeviceCompatibilityTests
    {
        [TestCase("Android OS 14 / API-34 (UP1A.231005.007)", CompatResult.Supported)]
        [TestCase("Android OS 15 / API-35 (AP31.240617.009)", CompatResult.Supported)]
        [TestCase("Android OS 13 / API-33 (TQ3A.230901.001)", CompatResult.Unsupported)]
        [TestCase("Android OS 10 / API-29 (QP1A.190711.020)", CompatResult.Unsupported)]
        [TestCase("un string sin el formato esperado", CompatResult.Unknown)]
        [TestCase("", CompatResult.Unknown)]
        [TestCase(null, CompatResult.Unknown)]
        public void ParseAndroid_ClasificaSegunApiLevel(string operatingSystem, CompatResult esperado)
        {
            Assert.AreEqual(esperado, DeviceCompatibility.ParseAndroid(operatingSystem));
        }

        [TestCase("iOS 17.0", CompatResult.Supported)]
        [TestCase("iOS 17.4.1", CompatResult.Supported)]
        [TestCase("iOS 18.2", CompatResult.Supported)]
        [TestCase("iPadOS 17.0", CompatResult.Supported)]
        [TestCase("iOS 16.4", CompatResult.Unsupported)]
        [TestCase("iOS 9.0", CompatResult.Unsupported)]
        [TestCase("un string sin el formato esperado", CompatResult.Unknown)]
        [TestCase("", CompatResult.Unknown)]
        [TestCase(null, CompatResult.Unknown)]
        public void ParseIos_ClasificaSegunVersionMayor(string operatingSystem, CompatResult esperado)
        {
            Assert.AreEqual(esperado, DeviceCompatibility.ParseIos(operatingSystem));
        }
    }
}
