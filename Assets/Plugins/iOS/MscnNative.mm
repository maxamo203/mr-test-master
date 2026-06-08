// Plugin iOS minimo: solo la hoja de compartir (UIActivityViewController).
//
// IMPORTANTE: NO subclaseamos UnityAppController. Hacerlo (IMPL_APP_CONTROLLER_SUBCLASS
// + override de didFinishLaunchingWithOptions/openURL) rompia el arranque de Unity 6
// (malloc: pointer being freed was not allocated, pantalla gris). La RECEPCION del
// archivo .mscn ("abrir con") se maneja desde C# con Application.deepLinkActivated /
// Application.absoluteURL, que Unity ya alimenta con la URL del archivo abierto.

#import <UIKit/UIKit.h>

extern "C" void _MscnShareFile(const char* cpath)
{
    if (cpath == NULL) return;
    NSString* path = [NSString stringWithUTF8String:cpath];
    NSURL* url = [NSURL fileURLWithPath:path];

    dispatch_async(dispatch_get_main_queue(), ^{
        // Root view controller actual (sin depender de headers de Unity).
        UIWindow* keyWindow = nil;
        for (UIWindow* w in [UIApplication sharedApplication].windows) {
            if (w.isKeyWindow) { keyWindow = w; break; }
        }
        if (keyWindow == nil) keyWindow = [UIApplication sharedApplication].windows.firstObject;

        UIViewController* root = keyWindow.rootViewController;
        if (root == nil) return;
        while (root.presentedViewController != nil) root = root.presentedViewController;

        UIActivityViewController* vc =
            [[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];

        // En iPad el popover necesita un anchor para no crashear.
        if (vc.popoverPresentationController != nil) {
            vc.popoverPresentationController.sourceView = root.view;
            vc.popoverPresentationController.sourceRect =
                CGRectMake(CGRectGetMidX(root.view.bounds), CGRectGetMidY(root.view.bounds), 1, 1);
            vc.popoverPresentationController.permittedArrowDirections = 0;
        }
        [root presentViewController:vc animated:YES completion:nil];
    });
}
