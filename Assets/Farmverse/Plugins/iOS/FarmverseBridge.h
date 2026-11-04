// One answer to one request from C# (Infrastructure/Unity/Native/NativeBridge.cs).
// error is NULL on success; otherwise cancelled | unsupported | unavailable | failed.
#pragma once
typedef void (*FarmverseCallback)(int requestId, const char* value, const char* error, const char* message);

#ifdef __OBJC__
#import <Foundation/Foundation.h>
static inline void FarmverseAnswer(FarmverseCallback callback, int requestId, NSString* value, NSString* error, NSString* message)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        callback(requestId, value.UTF8String, error.UTF8String, message.UTF8String);
    });
}
#endif
