// Plugin iOS para el archivo .MSCN:
//   1) _MscnShareFile(path): abre la hoja de compartir (UIActivityViewController).
//   2) Recibir "abrir con": una subclase de UnityAppController intercepta
//      application:openURL: y guarda el path; el lado C# (MscnReceiver) lo consume
//      por polling con _MscnConsumePendingFile(). Polling (en vez de UnitySendMessage)
//      para cubrir el cold-start, donde el runtime de scripting todavia no esta listo.
//
// Los document types (que la app aparezca en "Abrir con") los inyecta el
// IOSBuildPostProcessor en el Info.plist.

#import <UIKit/UIKit.h>
#import "UnityAppController.h"

static NSString* gPendingMscnPath = nil;

static void MscnSetPending(NSURL* url)
{
    if (url == nil || ![url isFileURL]) return;

    // Copiamos el archivo (suele venir de Documents/Inbox, de solo lectura) a un
    // temporal con lectura/escritura estable.
    NSString* src = [url path];
    NSString* dst = [NSTemporaryDirectory() stringByAppendingPathComponent:[src lastPathComponent]];
    NSFileManager* fm = [NSFileManager defaultManager];
    [fm removeItemAtPath:dst error:nil];
    NSError* err = nil;
    [fm copyItemAtPath:src toPath:dst error:&err];

    gPendingMscnPath = err ? src : dst;
}

extern "C" {

// Devuelve el path pendiente (y lo limpia), o "" si no hay. Buffer estatico para
// que el string siga valido tras limpiar gPendingMscnPath.
const char* _MscnConsumePendingFile()
{
    if (gPendingMscnPath == nil) return "";
    static char buf[2048];
    const char* s = [gPendingMscnPath UTF8String];
    strncpy(buf, s ? s : "", sizeof(buf) - 1);
    buf[sizeof(buf) - 1] = '\0';
    gPendingMscnPath = nil;
    return buf;
}

void _MscnShareFile(const char* cpath)
{
    if (cpath == NULL) return;
    NSString* path = [NSString stringWithUTF8String:cpath];
    NSURL* url = [NSURL fileURLWithPath:path];

    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = UnityGetGLViewController();
        if (root == nil) root = [[[UIApplication sharedApplication] keyWindow] rootViewController];
        if (root == nil) return;

        UIActivityViewController* vc =
            [[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];

        // En iPad el popover necesita un anchor para no crashear.
        if (vc.popoverPresentationController != nil) {
            vc.popoverPresentationController.sourceView = root.view;
            vc.popoverPresentationController.sourceRect =
                CGRectMake(root.view.bounds.size.width * 0.5, root.view.bounds.size.height * 0.5, 1, 1);
            vc.popoverPresentationController.permittedArrowDirections = 0;
        }
        [root presentViewController:vc animated:YES completion:nil];
    });
}

} // extern "C"

// Subclase de UnityAppController para interceptar la apertura de archivos.
@interface MscnAppController : UnityAppController
@end

@implementation MscnAppController

- (BOOL)application:(UIApplication*)application
            openURL:(NSURL*)url
            options:(NSDictionary<UIApplicationOpenURLOptionsKey, id>*)options
{
    MscnSetPending(url);
    return [super application:application openURL:url options:options];
}

// Cold-start: el archivo puede venir en las launch options.
- (BOOL)application:(UIApplication*)application didFinishLaunchingWithOptions:(NSDictionary*)launchOptions
{
    NSURL* url = launchOptions[UIApplicationLaunchOptionsURLKey];
    if (url != nil) MscnSetPending(url);
    return [super application:application didFinishLaunchingWithOptions:launchOptions];
}

@end

IMPL_APP_CONTROLLER_SUBCLASS(MscnAppController)
