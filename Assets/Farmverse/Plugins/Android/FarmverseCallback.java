package com.farmverse.nativebridge;

/** One answer to one request from C# (Infrastructure/Unity/Native/NativeBridge.cs implements it as an AndroidJavaProxy). */
public interface FarmverseCallback {
    void onSuccess(String value);

    /** code: cancelled | unsupported | unavailable | failed */
    void onError(String code, String message);
}
