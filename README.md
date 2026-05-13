# MR-Test

Aplicación de Mixed Reality para Android usando ARFoundation + ARCore con soporte para Google Cardboard.

## Requisitos

- **Unity** 6000.x (la misma versión con la que se generó el proyecto)
- **Android Build Support** instalado desde Unity Hub (incluye Android SDK, NDK y OpenJDK)
- **Dispositivo Android** con API 29 (Android 10) o superior y soporte ARCore
- **Google Cardboard** (opcional, para el modo VR estéreo)

## Pasos para compilar y correr

### 1. Clonar el repositorio

```bash
git clone <url-del-repo>
cd MR-Test
```

### 2. Abrir el proyecto en Unity

1. Abre **Unity Hub**
2. Haz clic en **Add > Add project from disk** y selecciona la carpeta `MR-Test`
3. Abre el proyecto con la versión de Unity correcta
4. Espera a que Unity importe los paquetes (primera apertura puede tardar varios minutos)

### 3. Configurar el build para Android

1. Ve a **File > Build Settings**
2. Selecciona **Android** como plataforma y haz clic en **Switch Platform**
3. Haz clic en **Player Settings** y verifica:
   - *Company Name* y *Product Name* según corresponda
   - *Minimum API Level*: Android 10 (API 29)
   - *Scripting Backend*: IL2CPP
   - *Target Architectures*: ARM64

### 4. Conectar el dispositivo

1. Activa **Depuración USB** en tu dispositivo Android (`Ajustes > Opciones de desarrollador`)
2. Conecta el dispositivo por USB
3. Verifica que aparezca con `adb devices`

### 5. Compilar e instalar

**Opción A — Desde el editor (build + instalación directa):**

1. En **File > Build Settings**, asegúrate de que tu dispositivo aparezca en *Run Device*
2. Haz clic en **Build And Run**
3. Elige una carpeta de destino para el `.apk` (p. ej. `build/`)

**Opción B — Generar el APK manualmente e instalar:**

```bash
# Instalar el APK en el dispositivo conectado
adb install -r ruta/al/archivo.apk
```

### 6. Permisos en el dispositivo

Al iniciar la app por primera vez, acepta el permiso de **cámara** cuando el sistema lo solicite. La app lo requiere para el feed de AR.

## Estructura del proyecto

```
Assets/
  GyroscopeTracking.cs        # Control de rotación por giroscopio
  MRCardboardController.cs    # Lógica principal MR + modo Cardboard
  Scenes/SampleScene.unity    # Escena principal
  XR/                         # Configuración ARCore / ARFoundation
Packages/                     # Dependencias de Unity Package Manager
ProjectSettings/              # Configuración del proyecto
```
