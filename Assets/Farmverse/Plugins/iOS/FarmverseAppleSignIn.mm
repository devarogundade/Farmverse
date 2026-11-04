// Sign in with Apple on iOS: the identity token Firebase Auth turns into an apple.com
// credential. No scopes are requested (no name, no email); the nonce arrives already
// SHA-256 hashed from C#, which keeps the raw one for Firebase.
#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>
#import "FarmverseBridge.h"

extern UIViewController* UnityGetGLViewController();

API_AVAILABLE(ios(13.0))
@interface FarmverseAppleRequest : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic) int requestId;
@property (nonatomic) FarmverseCallback callback;
@property (nonatomic, strong) ASAuthorizationController* controller;
@end

static NSMutableSet* FarmverseAppleRequests; // keeps each request alive until Apple answers

@implementation FarmverseAppleRequest

- (void)finishWith:(NSString*)value error:(NSString*)error message:(NSString*)message
{
    FarmverseAnswer(self.callback, self.requestId, value, error, message);
    [FarmverseAppleRequests removeObject:self];
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithAuthorization:(ASAuthorization*)authorization
{
    ASAuthorizationAppleIDCredential* credential = (ASAuthorizationAppleIDCredential*)authorization.credential;
    if (![credential isKindOfClass:[ASAuthorizationAppleIDCredential class]] || credential.identityToken == nil) {
        [self finishWith:nil error:@"failed" message:@"no identity token"];
        return;
    }
    NSString* token = [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding];
    [self finishWith:token error:nil message:nil];
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithError:(NSError*)error
{
    BOOL cancelled = error.code == ASAuthorizationErrorCanceled;
    [self finishWith:nil error:(cancelled ? @"cancelled" : @"failed") message:error.localizedDescription];
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
{
    return UnityGetGLViewController().view.window;
}

@end

extern "C" void FarmverseAppleSignIn(int requestId, const char* hashedNonce, FarmverseCallback callback)
{
    NSString* nonce = [NSString stringWithUTF8String:hashedNonce];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 13.0, *)) {
            if (FarmverseAppleRequests == nil) FarmverseAppleRequests = [NSMutableSet set];
            ASAuthorizationAppleIDRequest* request = [[[ASAuthorizationAppleIDProvider alloc] init] createRequest];
            request.requestedScopes = @[];
            request.nonce = nonce;
            FarmverseAppleRequest* pending = [[FarmverseAppleRequest alloc] init];
            pending.requestId = requestId;
            pending.callback = callback;
            pending.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
            pending.controller.delegate = pending;
            pending.controller.presentationContextProvider = pending;
            [FarmverseAppleRequests addObject:pending];
            [pending.controller performRequests];
        } else {
            FarmverseAnswer(callback, requestId, nil, @"unsupported", @"Sign in with Apple needs iOS 13");
        }
    });
}
