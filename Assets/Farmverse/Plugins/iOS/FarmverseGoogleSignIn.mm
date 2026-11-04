// Google Sign-In on iOS (GoogleSignIn pod, see Editor/FarmverseDependencies.xml): an ID
// token and access token Firebase Auth links to the player's account. The client id
// comes from GoogleService-Info.plist; its REVERSED_CLIENT_ID URL scheme is added to
// Info.plist at build time (Editor/FarmverseIosBuild.cs).
#import <UIKit/UIKit.h>
#import <GoogleSignIn/GoogleSignIn.h>
#import "FarmverseBridge.h"
#import "AppDelegateListener.h"

extern UIViewController* UnityGetGLViewController();

// lets GoogleSignIn finish when the app is reopened from its web flow
@interface FarmverseGoogleUrlListener : NSObject <AppDelegateListener>
@end

@implementation FarmverseGoogleUrlListener
- (void)onOpenURL:(NSNotification*)notification
{
    NSURL* url = notification.userInfo[@"url"];
    if (url != nil) [GIDSignIn.sharedInstance handleURL:url];
}
@end

static FarmverseGoogleUrlListener* FarmverseGoogleListener;

static NSString* FarmverseJson(NSDictionary* values)
{
    NSData* data = [NSJSONSerialization dataWithJSONObject:values options:0 error:nil];
    return [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
}

extern "C" void FarmverseGoogleSignIn(int requestId, FarmverseCallback callback)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (FarmverseGoogleListener == nil) {
            FarmverseGoogleListener = [[FarmverseGoogleUrlListener alloc] init];
            UnityRegisterAppDelegateListener(FarmverseGoogleListener);
        }
        NSString* path = [[NSBundle mainBundle] pathForResource:@"GoogleService-Info" ofType:@"plist"];
        NSString* clientId = path != nil ? [NSDictionary dictionaryWithContentsOfFile:path][@"CLIENT_ID"] : nil;
        if (clientId.length == 0) {
            FarmverseAnswer(callback, requestId, nil, @"unavailable", @"CLIENT_ID missing from GoogleService-Info.plist: enable Google sign-in and download it again");
            return;
        }
        GIDSignIn.sharedInstance.configuration = [[GIDConfiguration alloc] initWithClientID:clientId];
        [GIDSignIn.sharedInstance signInWithPresentingViewController:UnityGetGLViewController()
                                                          completion:^(GIDSignInResult* result, NSError* error) {
            if (error != nil) {
                BOOL cancelled = error.code == kGIDSignInErrorCodeCanceled;
                FarmverseAnswer(callback, requestId, nil, cancelled ? @"cancelled" : @"failed", error.localizedDescription);
                return;
            }
            NSString* idToken = result.user.idToken.tokenString;
            if (idToken.length == 0) {
                FarmverseAnswer(callback, requestId, nil, @"failed", @"no ID token");
                return;
            }
            FarmverseAnswer(callback, requestId, FarmverseJson(@{ @"idToken": idToken, @"accessToken": result.user.accessToken.tokenString ?: @"" }), nil, nil);
        }];
    });
}
