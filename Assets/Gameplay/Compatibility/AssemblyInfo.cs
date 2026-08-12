using System.Runtime.CompilerServices;

// Permite que Mortuorium.Tests.EditMode llame a los métodos 'internal' de parseo
// (DeviceCompatibility.ParseAndroid/ParseIos) sin tener que hacerlos públicos.
[assembly: InternalsVisibleTo("Mortuorium.Tests.EditMode")]
