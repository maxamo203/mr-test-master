// Plugin nativo de LiDAR para iPhone Pro / iPad Pro.
//
// NO usa ARFoundation para nada del LiDAR: trabaja directo contra la ARSession
// real de ARKit. El puntero a la sesion llega desde C# via
// XRSessionSubsystem.nativePtr (struct UnityXRNativeSession), que es el
// mecanismo documentado para extender ARKit por debajo de Unity.
//
// Funciones expuestas a C# (ver Assets/Scanner/NativeLidar.cs):
//   _LidarSetSession        — registra el ARSession nativo.
//   _LidarIsSupported       — el dispositivo tiene LiDAR (sceneDepth / mesh).
//   _LidarEnsureConfig      — inyecta sceneDepth + sceneReconstruction en la
//                             configuracion actual y re-corre la sesion. Es
//                             idempotente: si los flags ya estan, no hace nada.
//                             (ARFoundation puede pisar la config al re-crear
//                             el image tracking; C# la re-asegura a 1 Hz.)
//   _LidarRaycast           — raycast de ARKit (depth-aware con LiDAR) desde un
//                             punto de pantalla. Devuelve pos + normal en
//                             coordenadas de Unity (Z invertida).
//   _LidarCapturePoints     — muestrea el depthMap de sceneDepth y devuelve
//                             puntos 3D en world-space de Unity (para la nube
//                             de puntos del mapeo).

#import <ARKit/ARKit.h>
#import <UIKit/UIKit.h>
#import <simd/simd.h>

// Layout del struct que devuelve XRSessionSubsystem.nativePtr (Unity ARKit XR Plugin).
typedef struct UnityXRNativeSession
{
    int   version;
    void* sessionPtr;
} UnityXRNativeSession;

static __weak ARSession* g_lidarSession = nil;

static ARSession* LidarGetSession(void)
{
    return g_lidarSession;
}

// Orientacion de interfaz actual (para displayTransformForOrientation).
static UIInterfaceOrientation LidarInterfaceOrientation(void)
{
    for (UIScene* scene in [UIApplication sharedApplication].connectedScenes)
    {
        if ([scene isKindOfClass:[UIWindowScene class]])
            return ((UIWindowScene*)scene).interfaceOrientation;
    }
    return UIInterfaceOrientationPortrait;
}

extern "C" void _LidarSetSession(void* nativeSessionStruct)
{
    if (nativeSessionStruct == NULL) { g_lidarSession = nil; return; }
    UnityXRNativeSession* native = (UnityXRNativeSession*)nativeSessionStruct;
    g_lidarSession = (__bridge ARSession*)native->sessionPtr;
}

extern "C" int _LidarIsSupported(void)
{
    if (@available(iOS 14.0, *))
    {
        bool depth = [ARWorldTrackingConfiguration supportsFrameSemantics:ARFrameSemanticSceneDepth];
        bool mesh  = [ARWorldTrackingConfiguration supportsSceneReconstruction:ARSceneReconstructionMesh];
        return (depth || mesh) ? 1 : 0;
    }
    return 0;
}

// Devuelve: 0 = config ya estaba OK, 1 = config re-aplicada,
//           <0 = no se pudo (sin sesion / config no world-tracking / iOS viejo).
extern "C" int _LidarEnsureConfig(void)
{
    if (@available(iOS 14.0, *))
    {
        ARSession* session = LidarGetSession();
        if (session == nil) return -1;

        ARConfiguration* current = session.configuration;
        if (![current isKindOfClass:[ARWorldTrackingConfiguration class]]) return -2;
        ARWorldTrackingConfiguration* cfg = (ARWorldTrackingConfiguration*)current;

        bool wantDepth = [ARWorldTrackingConfiguration supportsFrameSemantics:ARFrameSemanticSmoothedSceneDepth];
        bool wantMesh  = [ARWorldTrackingConfiguration supportsSceneReconstruction:ARSceneReconstructionMesh];
        if (!wantDepth && !wantMesh) return -3;

        bool hasDepth = !wantDepth || (cfg.frameSemantics & ARFrameSemanticSmoothedSceneDepth) != 0;
        bool hasMesh  = !wantMesh  || (cfg.sceneReconstruction & ARSceneReconstructionMesh) != 0;
        if (hasDepth && hasMesh) return 0;

        ARWorldTrackingConfiguration* copy = [cfg copy];
        if (wantDepth) copy.frameSemantics |= (ARFrameSemanticSceneDepth | ARFrameSemanticSmoothedSceneDepth);
        if (wantMesh)  copy.sceneReconstruction = ARSceneReconstructionMesh;
        // Sin reset options: mantiene el mapa SLAM, anchors e imagenes tal cual.
        [session runWithConfiguration:copy];
        return 1;
    }
    return -4;
}

// vx, vy: punto normalizado de pantalla (0..1) con origen ARRIBA-izquierda
// (convencion UIKit; C# convierte desde la de Unity). vpW/vpH: tamano del
// viewport en pixeles. outPosNormal: 6 floats [px,py,pz, nx,ny,nz] en
// coordenadas de Unity. Devuelve 1 si hubo hit.
extern "C" int _LidarRaycast(float vx, float vy, float vpW, float vpH, float* outPosNormal)
{
    ARSession* session = LidarGetSession();
    if (session == nil || outPosNormal == NULL) return 0;
    ARFrame* frame = session.currentFrame;
    if (frame == nil) return 0;

    // displayTransform mapea coords normalizadas de IMAGEN -> VIEWPORT;
    // invertimos para ir de pantalla a imagen capturada.
    CGAffineTransform display = [frame displayTransformForOrientation:LidarInterfaceOrientation()
                                                          viewportSize:CGSizeMake(vpW, vpH)];
    CGPoint imgPoint = CGPointApplyAffineTransform(CGPointMake(vx, vy),
                                                   CGAffineTransformInvert(display));
    if (imgPoint.x < 0.0 || imgPoint.x > 1.0 || imgPoint.y < 0.0 || imgPoint.y > 1.0) return 0;

    // estimatedPlane: en dispositivos LiDAR el raycast es depth-aware (snapea a
    // la geometria real, mas aun con sceneReconstruction activo).
    ARRaycastQuery* query = [frame raycastQueryFromPoint:imgPoint
                                          allowingTarget:ARRaycastTargetEstimatedPlane
                                               alignment:ARRaycastTargetAlignmentAny];
    NSArray<ARRaycastResult*>* results = [session raycast:query];
    if (results.count == 0) return 0;

    simd_float4x4 m = results.firstObject.worldTransform;
    // ARKit es right-handed; Unity invierte Z (misma convencion que usa el
    // ARKit XR Plugin para el session space).
    outPosNormal[0] =  m.columns[3].x;
    outPosNormal[1] =  m.columns[3].y;
    outPosNormal[2] = -m.columns[3].z;
    // La normal de la superficie es el eje Y local del resultado.
    outPosNormal[3] =  m.columns[1].x;
    outPosNormal[4] =  m.columns[1].y;
    outPosNormal[5] = -m.columns[1].z;
    return 1;
}

// Muestrea el depthMap (smoothedSceneDepth si esta, sino sceneDepth) cada
// `step` pixeles y unproyecta con las intrinsics de la camara. Escribe hasta
// maxPoints triples (x,y,z world-space de Unity) en outBuffer.
//   minConfidence: 0=low 1=medium 2=high (ARConfidenceLevel).
//   maxDepth: descarta muestras mas lejos que esto (metros).
// Devuelve la cantidad de puntos escritos.
extern "C" int _LidarCapturePoints(float* outBuffer, int maxPoints, int step,
                                   int minConfidence, float maxDepth)
{
    if (@available(iOS 14.0, *))
    {
        if (outBuffer == NULL || maxPoints <= 0) return 0;
        if (step < 1) step = 1;

        ARSession* session = LidarGetSession();
        if (session == nil) return 0;
        ARFrame* frame = session.currentFrame;
        if (frame == nil) return 0;

        ARDepthData* depth = frame.smoothedSceneDepth ?: frame.sceneDepth;
        if (depth == nil) return 0;

        CVPixelBufferRef depthMap = depth.depthMap;
        CVPixelBufferRef confMap  = depth.confidenceMap; // puede ser NULL
        if (depthMap == NULL) return 0;

        CVPixelBufferLockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
        if (confMap) CVPixelBufferLockBaseAddress(confMap, kCVPixelBufferLock_ReadOnly);

        size_t   dw      = CVPixelBufferGetWidth(depthMap);
        size_t   dh      = CVPixelBufferGetHeight(depthMap);
        size_t   dStride = CVPixelBufferGetBytesPerRow(depthMap);
        uint8_t* dBase   = (uint8_t*)CVPixelBufferGetBaseAddress(depthMap);

        size_t   cStride = confMap ? CVPixelBufferGetBytesPerRow(confMap) : 0;
        uint8_t* cBase   = confMap ? (uint8_t*)CVPixelBufferGetBaseAddress(confMap) : NULL;

        // Intrinsics vienen en resolucion de imagen capturada; el depth map es
        // mas chico — escalamos las coords de pixel.
        simd_float3x3 K   = frame.camera.intrinsics;
        CGSize imgRes     = frame.camera.imageResolution;
        float scaleX      = (float)imgRes.width  / (float)dw;
        float scaleY      = (float)imgRes.height / (float)dh;
        float fx = K.columns[0].x, fy = K.columns[1].y;
        float cx = K.columns[2].x, cy = K.columns[2].y;
        simd_float4x4 camT = frame.camera.transform;

        int written = 0;
        for (size_t y = (size_t)step / 2; y < dh && written < maxPoints; y += (size_t)step)
        {
            float*   dRow = (float*)(dBase + y * dStride);
            uint8_t* cRow = cBase ? (cBase + y * cStride) : NULL;
            for (size_t x = (size_t)step / 2; x < dw && written < maxPoints; x += (size_t)step)
            {
                float d = dRow[x];
                if (!isfinite(d) || d < 0.1f || d > maxDepth) continue;
                if (cRow && cRow[x] < (uint8_t)minConfidence) continue;

                // Pixel del depth map -> pixel de imagen capturada -> rayo de camara.
                float u = ((float)x + 0.5f) * scaleX;
                float v = ((float)y + 0.5f) * scaleY;
                // Espacio de camara ARKit: +X derecha, +Y arriba, camara mira -Z.
                // La imagen tiene Y hacia abajo => se invierte el termino Y.
                simd_float4 pCam = simd_make_float4(
                    (u - cx) / fx * d,
                   -((v - cy) / fy) * d,
                   -d,
                    1.0f);
                simd_float4 pWorld = simd_mul(camT, pCam);

                outBuffer[written * 3 + 0] =  pWorld.x;
                outBuffer[written * 3 + 1] =  pWorld.y;
                outBuffer[written * 3 + 2] = -pWorld.z; // ARKit -> Unity
                written++;
            }
        }

        if (confMap) CVPixelBufferUnlockBaseAddress(confMap, kCVPixelBufferLock_ReadOnly);
        CVPixelBufferUnlockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
        return written;
    }
    return 0;
}
