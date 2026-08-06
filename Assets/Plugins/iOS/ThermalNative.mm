#import <Foundation/Foundation.h>

// Estado termico del dispositivo. Unity NO expone NSProcessInfo.thermalState en su
// API, y es la senal mas directa para entender el consumo: cuando iOS pasa a
// "serious" empieza a bajar los clocks del SoC, y ahi la caida de fps y el drenaje
// de bateria se explican solos. Sin este dato uno mira los fps caer sin saber si es
// el codigo o el throttling termico.
//
// Lo consume DebugHud/PowerProbe.cs (solo development build).
extern "C" int _MortuoriumThermalState()
{
    if (@available(iOS 11.0, *))
    {
        switch ([[NSProcessInfo processInfo] thermalState])
        {
            case NSProcessInfoThermalStateNominal:  return 0;
            case NSProcessInfoThermalStateFair:     return 1;
            case NSProcessInfoThermalStateSerious:  return 2;
            case NSProcessInfoThermalStateCritical: return 3;
        }
    }
    return -1;   // desconocido
}

// Modo de bajo consumo: si esta prendido, iOS ya limita CPU/GPU y refresco de
// pantalla por su cuenta, asi que cualquier medicion de rendimiento queda sesgada.
extern "C" int _MortuoriumLowPowerMode()
{
    if (@available(iOS 9.0, *))
        return [[NSProcessInfo processInfo] isLowPowerModeEnabled] ? 1 : 0;
    return -1;
}
