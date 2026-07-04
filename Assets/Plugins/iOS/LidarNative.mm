// Plugin nativo de LiDAR para iPhone Pro / iPad Pro.
//
// NO usa ARFoundation para nada del LiDAR: habla directo con la ARSession real
// de ARKit. El puntero a la sesion llega desde C# via XRSessionSubsystem.nativePtr
// (struct UnityXRNativeSession), el mecanismo documentado para extender ARKit
// por debajo de Unity.
//
// IMPORTANTE (coordenadas): todo lo que sale de aca esta en SESSION SPACE de
// ARKit convertido a mano izquierda (z invertida). El C# (NativeLidar) lo
// transforma despues por XROrigin.TrackablesParent para llevarlo al world de
// Unity — el XROrigin aplica un CameraYOffset en modo Device, asi que NO se
// puede usar el valor crudo.
//
// Funciones expuestas a C# (ver Assets/Scanner/NativeLidar.cs):
//   _LidarSetSession     — registra el ARSession nativo.
//   _LidarIsSupported    — el dispositivo soporta sceneDepth (LiDAR).
//   _LidarEnsureConfig   — agrega sceneDepth a la config si falta (idempotente;
//                          si el AROcclusionManager ya lo pidio, no toca nada).
//   _LidarRaycast        — punto fisico real bajo un punto de pantalla:
//                          muestrea el depthMap y unproyecta (normal por
//                          gradiente de depth). Fallback: ARRaycastQuery.
//   _LidarCapturePoints  — muestrea el depthMap en grilla para la nube de puntos.
//   _LidarGetStatus      — flags de diagnostico para la UI.

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
        return [ARWorldTrackingConfiguration supportsFrameSemantics:ARFrameSemanticSceneDepth] ? 1 : 0;
    return 0;
}

// Devuelve: 0 = config ya tiene sceneDepth (no toca nada), 1 = re-aplicada,
//           <0 = no se pudo (sin sesion / config no world-tracking / sin soporte).
// Solo pide frameSemantics de depth — NO sceneReconstruction: el raycast ahora
// muestrea el depthMap directo y el mesh solo generaria ARMeshAnchors que nadie
// usa (y mas motivos de conflicto con la config que maneja ARFoundation).
extern "C" int _LidarEnsureConfig(void)
{
    if (@available(iOS 14.0, *))
    {
        ARSession* session = g_lidarSession;
        if (session == nil) return -1;
        if (![ARWorldTrackingConfiguration supportsFrameSemantics:ARFrameSemanticSceneDepth]) return -3;

        ARConfiguration* current = session.configuration;
        if (![current isKindOfClass:[ARWorldTrackingConfiguration class]]) return -2;
        ARWorldTrackingConfiguration* cfg = (ARWorldTrackingConfiguration*)current;

        // Con sceneDepth ya activo alcanza (el AROcclusionManager de la escena
        // suele pedirlo el solo): capture/raycast usan sceneDepth y, si esta,
        // smoothedSceneDepth. No re-corremos la sesion al pedo.
        if ((cfg.frameSemantics & ARFrameSemanticSceneDepth) != 0) return 0;

        ARWorldTrackingConfiguration* copy = [cfg copy];
        copy.frameSemantics |= ARFrameSemanticSceneDepth;
        if ([ARWorldTrackingConfiguration supportsFrameSemantics:ARFrameSemanticSmoothedSceneDepth])
            copy.frameSemantics |= ARFrameSemanticSmoothedSceneDepth;
        // Sin reset options: mantiene el mapa SLAM, anchors e imagenes tal cual.
        [session runWithConfiguration:copy];
        return 1;
    }
    return -4;
}

// ── Helpers de depth ─────────────────────────────────────────────────────────

// Unproyecta el pixel (x,y) del depth map a session space de ARKit (mano
// derecha). Requiere el depthMap lockeado. Devuelve false si el depth es invalido.
static bool UnprojectDepthPixel(ARCamera* camera, uint8_t* dBase, size_t dStride,
                                size_t dw, size_t dh, size_t x, size_t y,
                                float maxDepth, simd_float3* outWorld)
{
    if (x >= dw || y >= dh) return false;
    float d = ((float*)(dBase + y * dStride))[x];
    if (!isfinite(d) || d < 0.1f || d > maxDepth) return false;

    // Intrinsics vienen en resolucion de imagen capturada; el depth map es mas
    // chico — escalamos las coords de pixel.
    simd_float3x3 K = camera.intrinsics;
    CGSize imgRes   = camera.imageResolution;
    float u = ((float)x + 0.5f) * (float)imgRes.width  / (float)dw;
    float v = ((float)y + 0.5f) * (float)imgRes.height / (float)dh;
    float fx = K.columns[0].x, fy = K.columns[1].y;
    float cx = K.columns[2].x, cy = K.columns[2].y;

    // Espacio de camara ARKit: +X derecha, +Y arriba, la camara mira -Z.
    // La imagen tiene Y hacia abajo => se invierte el termino Y.
    simd_float4 pCam = simd_make_float4((u - cx) / fx * d, -((v - cy) / fy) * d, -d, 1.0f);
    simd_float4 pW   = simd_mul(camera.transform, pCam);
    *outWorld = simd_make_float3(pW.x, pW.y, pW.z);
    return true;
}

// ARKit (mano derecha) -> Unity session space (mano izquierda): z invertida.
static inline simd_float3 ToUnity(simd_float3 v) { return simd_make_float3(v.x, v.y, -v.z); }

// vx, vy: punto normalizado de pantalla (0..1) con origen ARRIBA-izquierda
// (convencion UIKit; C# convierte desde la de Unity). vpW/vpH: viewport en px.
// outPosNormal: 6 floats [px,py,pz, nx,ny,nz] en SESSION space de Unity.
// Devuelve 1 = hit por depth map, 2 = hit por ARRaycastQuery, 0 = nada.
extern "C" int _LidarRaycast(float vx, float vy, float vpW, float vpH, float* outPosNormal)
{
    ARSession* session = g_lidarSession;
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

    // ── 1) Muestreo directo del depth map: el punto FISICO real, sin plane
    //       fitting. La normal sale del gradiente de depth alrededor del pixel.
    //       Se prefiere el depth CRUDO: smoothedSceneDepth promedia frames
    //       anteriores y viene "atrasado" respecto a la pose del frame — con la
    //       camara en movimiento eso desplaza los puntos del mundo fisico.
    if (@available(iOS 14.0, *))
    {
        ARDepthData* depth = frame.sceneDepth ?: frame.smoothedSceneDepth;
        if (depth != nil && depth.depthMap != NULL)
        {
            CVPixelBufferRef depthMap = depth.depthMap;
            CVPixelBufferLockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);

            size_t   dw      = CVPixelBufferGetWidth(depthMap);
            size_t   dh      = CVPixelBufferGetHeight(depthMap);
            size_t   dStride = CVPixelBufferGetBytesPerRow(depthMap);
            uint8_t* dBase   = (uint8_t*)CVPixelBufferGetBaseAddress(depthMap);

            size_t px = (size_t)MIN(MAX(imgPoint.x * dw, 0.0), (double)(dw - 1));
            size_t py = (size_t)MIN(MAX(imgPoint.y * dh, 0.0), (double)(dh - 1));

            simd_float3 pC;
            bool ok = UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, px, py, 10.0f, &pC);
            if (ok)
            {
                simd_float4 camCol = frame.camera.transform.columns[3];
                simd_float3 camPos = ToUnity(simd_make_float3(camCol.x, camCol.y, camCol.z));
                simd_float3 pU     = ToUnity(pC);

                // Normal por gradiente: vecinos a +-k pixeles. Todo se pasa a
                // coords de Unity ANTES del cross (el flip de Z invierte la
                // quiralidad; cruzar en el espacio ya flipeado evita el lio de
                // pseudovectores). El signo final se fija mirando a la camara.
                size_t k = MAX((size_t)2, dw / 64);
                simd_float3 pL, pR, pT, pB;
                bool okN = px >= k && py >= k
                        && UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, px - k, py, 10.0f, &pL)
                        && UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, px + k, py, 10.0f, &pR)
                        && UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, px, py - k, 10.0f, &pT)
                        && UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, px, py + k, 10.0f, &pB);

                simd_float3 n;
                if (okN)
                {
                    n = simd_cross(ToUnity(pR) - ToUnity(pL), ToUnity(pB) - ToUnity(pT));
                    float len = simd_length(n);
                    n = len > 1e-6f ? n / len : (camPos - pU) / simd_length(camPos - pU);
                }
                else
                {
                    n = camPos - pU;
                    n = n / simd_length(n);
                }
                // La normal siempre mirando hacia la camara.
                if (simd_dot(n, camPos - pU) < 0.0f) n = -n;

                outPosNormal[0] = pU.x; outPosNormal[1] = pU.y; outPosNormal[2] = pU.z;
                outPosNormal[3] = n.x;  outPosNormal[4] = n.y;  outPosNormal[5] = n.z;
                CVPixelBufferUnlockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
                return 1;
            }
            CVPixelBufferUnlockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
        }
    }

    // ── 2) Fallback: raycast de ARKit contra plano estimado.
    ARRaycastQuery* query = [frame raycastQueryFromPoint:imgPoint
                                          allowingTarget:ARRaycastTargetEstimatedPlane
                                               alignment:ARRaycastTargetAlignmentAny];
    NSArray<ARRaycastResult*>* results = [session raycast:query];
    if (results.count == 0) return 0;

    simd_float4x4 m = results.firstObject.worldTransform;
    outPosNormal[0] =  m.columns[3].x;
    outPosNormal[1] =  m.columns[3].y;
    outPosNormal[2] = -m.columns[3].z;
    // La normal de la superficie es el eje Y local del resultado.
    outPosNormal[3] =  m.columns[1].x;
    outPosNormal[4] =  m.columns[1].y;
    outPosNormal[5] = -m.columns[1].z;
    return 2;
}

// Muestrea el depthMap CRUDO (sceneDepth; smoothed solo como fallback — el
// suavizado temporal corre los puntos cuando la camara se mueve) cada `step`
// pixeles y unproyecta con las intrinsics de la camara. Escribe hasta
// maxPoints triples (x,y,z en SESSION space de Unity) en outBuffer.
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

        ARSession* session = g_lidarSession;
        if (session == nil) return 0;
        ARFrame* frame = session.currentFrame;
        if (frame == nil) return 0;

        // Con tracking degradado (relocalizando, poca luz, movimiento brusco) la
        // pose de la camara no es confiable: capturar ahora mete capas corridas.
        if (frame.camera.trackingState != ARTrackingStateNormal) return 0;

        ARDepthData* depth = frame.sceneDepth ?: frame.smoothedSceneDepth;
        if (depth == nil || depth.depthMap == NULL) return 0;

        CVPixelBufferRef depthMap = depth.depthMap;
        CVPixelBufferRef confMap  = depth.confidenceMap; // puede ser NULL
        CVPixelBufferLockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
        if (confMap) CVPixelBufferLockBaseAddress(confMap, kCVPixelBufferLock_ReadOnly);

        size_t   dw      = CVPixelBufferGetWidth(depthMap);
        size_t   dh      = CVPixelBufferGetHeight(depthMap);
        size_t   dStride = CVPixelBufferGetBytesPerRow(depthMap);
        uint8_t* dBase   = (uint8_t*)CVPixelBufferGetBaseAddress(depthMap);

        size_t   cStride = confMap ? CVPixelBufferGetBytesPerRow(confMap) : 0;
        uint8_t* cBase   = confMap ? (uint8_t*)CVPixelBufferGetBaseAddress(confMap) : NULL;

        int written = 0;
        for (size_t y = (size_t)step / 2; y < dh && written < maxPoints; y += (size_t)step)
        {
            uint8_t* cRow = cBase ? (cBase + y * cStride) : NULL;
            for (size_t x = (size_t)step / 2; x < dw && written < maxPoints; x += (size_t)step)
            {
                if (cRow && cRow[x] < (uint8_t)minConfidence) continue;
                simd_float3 pW;
                if (!UnprojectDepthPixel(frame.camera, dBase, dStride, dw, dh, x, y, maxDepth, &pW))
                    continue;
                simd_float3 pU = ToUnity(pW);
                outBuffer[written * 3 + 0] = pU.x;
                outBuffer[written * 3 + 1] = pU.y;
                outBuffer[written * 3 + 2] = pU.z;
                written++;
            }
        }

        if (confMap) CVPixelBufferUnlockBaseAddress(confMap, kCVPixelBufferLock_ReadOnly);
        CVPixelBufferUnlockBaseAddress(depthMap, kCVPixelBufferLock_ReadOnly);
        return written;
    }
    return 0;
}

// Diagnostico para la UI. Llena out (min 8 ints):
//   [0] hay sesion            [1] hay currentFrame
//   [2] config es WorldTracking [3] frameSemantics tiene sceneDepth
//   [4] frameSemantics tiene smoothedSceneDepth
//   [5] frame.sceneDepth != nil [6] frame.smoothedSceneDepth != nil
//   [7] ancho del depth map (0 si no hay)
extern "C" void _LidarGetStatus(int* out, int n)
{
    if (out == NULL || n < 8) return;
    for (int i = 0; i < 8; i++) out[i] = 0;

    ARSession* session = g_lidarSession;
    if (session == nil) return;
    out[0] = 1;

    ARConfiguration* cfg = session.configuration;
    if ([cfg isKindOfClass:[ARWorldTrackingConfiguration class]]) out[2] = 1;

    ARFrame* frame = session.currentFrame;
    if (frame == nil) return;
    out[1] = 1;

    if (@available(iOS 14.0, *))
    {
        if (cfg != nil)
        {
            out[3] = (cfg.frameSemantics & ARFrameSemanticSceneDepth) != 0 ? 1 : 0;
            out[4] = (cfg.frameSemantics & ARFrameSemanticSmoothedSceneDepth) != 0 ? 1 : 0;
        }
        out[5] = frame.sceneDepth != nil ? 1 : 0;
        out[6] = frame.smoothedSceneDepth != nil ? 1 : 0;
        ARDepthData* depth = frame.sceneDepth ?: frame.smoothedSceneDepth;
        if (depth != nil && depth.depthMap != NULL)
            out[7] = (int)CVPixelBufferGetWidth(depth.depthMap);
    }
}
