// The share sheet for a text file (the data export).
#import <UIKit/UIKit.h>
#import "FarmverseBridge.h"

extern UIViewController* UnityGetGLViewController();

extern "C" void FarmverseShareText(int requestId, const char* fileName, const char* content, const char* title, FarmverseCallback callback)
{
    NSString* name = [[NSString stringWithUTF8String:fileName] lastPathComponent];
    NSString* text = [NSString stringWithUTF8String:content];
    NSString* subject = [NSString stringWithUTF8String:title];
    dispatch_async(dispatch_get_main_queue(), ^{
        NSURL* url = [[NSURL fileURLWithPath:NSTemporaryDirectory()] URLByAppendingPathComponent:name];
        NSError* error = nil;
        if (![text writeToURL:url atomically:YES encoding:NSUTF8StringEncoding error:&error]) {
            FarmverseAnswer(callback, requestId, nil, @"failed", error.localizedDescription ?: @"write failed");
            return;
        }
        UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];
        [sheet setValue:subject forKey:@"subject"];
        UIViewController* host = UnityGetGLViewController();
        // iPad presents the sheet as a popover, which needs an anchor
        sheet.popoverPresentationController.sourceView = host.view;
        sheet.popoverPresentationController.sourceRect = CGRectMake(CGRectGetMidX(host.view.bounds), CGRectGetMidY(host.view.bounds), 1, 1);
        sheet.popoverPresentationController.permittedArrowDirections = 0;
        sheet.completionWithItemsHandler = ^(UIActivityType type, BOOL completed, NSArray* items, NSError* failure) {
            if (failure != nil) FarmverseAnswer(callback, requestId, nil, @"failed", failure.localizedDescription);
            else if (completed) FarmverseAnswer(callback, requestId, @"", nil, nil);
            else FarmverseAnswer(callback, requestId, nil, @"cancelled", @"cancelled");
        };
        [host presentViewController:sheet animated:YES completion:nil];
    });
}
