#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity.Native
{
    /// <summary>What a native sign-in hands to Firebase Auth.</summary>
    public sealed class NativeTokens
    {
        public NativeTokens(string idToken, string? accessToken, string? rawNonce)
        {
            IdToken = idToken;
            AccessToken = accessToken;
            RawNonce = rawNonce;
        }

        public string IdToken { get; }
        public string? AccessToken { get; }
        /// <summary>Apple: the unhashed nonce whose SHA-256 went into the request (Firebase checks it).</summary>
        public string? RawNonce { get; }
    }

    /// <summary>
    /// The token steps Firebase Auth cannot do itself in Unity: Google's ID token
    /// (Android Credential Manager, iOS GoogleSignIn) and Apple's identity token
    /// on iOS (AuthenticationServices). Apple on Android needs none of this — it
    /// uses Firebase's own OAuth flow. No scopes are asked for: no name, no email.
    /// </summary>
    public static class NativeSignIn
    {
        public static bool GoogleAvailable =>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static bool AppleTokenAvailable =>
#if UNITY_IOS && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static async Task<NativeTokens> Google(CancellationToken cancellation)
        {
            NativeBridge.Initialize();
#if UNITY_ANDROID && !UNITY_EDITOR
            var (id, answer) = NativeBridge.Begin(cancellation);
            using (var activity = NativeBridge.CurrentActivity())
            using (var google = new AndroidJavaClass(NativeBridge.AndroidPackage + ".FarmverseGoogleSignIn"))
                google.CallStatic("signIn", activity, new NativeBridge.AndroidCallback(id));
            return new NativeTokens(await answer, null, null);
#elif UNITY_IOS && !UNITY_EDITOR
            var (id, answer) = NativeBridge.Begin(cancellation);
            FarmverseGoogleSignIn(id, NativeBridge.OnIosAnswer);
            var tokens = JsonUtility.FromJson<IosTokens>(await answer);
            return new NativeTokens(tokens.idToken, string.IsNullOrEmpty(tokens.accessToken) ? null : tokens.accessToken, null);
#else
            await Task.CompletedTask;
            throw NativeBridge.NotOnThisPlatform("Google sign-in");
#endif
        }

        public static async Task<NativeTokens> Apple(CancellationToken cancellation)
        {
            NativeBridge.Initialize();
#if UNITY_IOS && !UNITY_EDITOR
            var rawNonce = RandomNonce();
            var (id, answer) = NativeBridge.Begin(cancellation);
            FarmverseAppleSignIn(id, Sha256Hex(rawNonce), NativeBridge.OnIosAnswer);
            return new NativeTokens(await answer, null, rawNonce);
#else
            await Task.CompletedTask;
            throw NativeBridge.NotOnThisPlatform("Sign in with Apple (native)");
#endif
        }

        /// <summary>32 url-safe random characters (Firebase's recommended nonce shape).</summary>
        public static string RandomNonce()
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvwxyz-._";
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var text = new StringBuilder(bytes.Length);
            foreach (var b in bytes) text.Append(alphabet[b % alphabet.Length]);
            return text.ToString();
        }

        public static string Sha256Hex(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) text.Append(b.ToString("x2"));
            return text.ToString();
        }

#if UNITY_IOS && !UNITY_EDITOR
        [Serializable]
        private sealed class IosTokens
        {
            public string idToken = string.Empty;
            public string accessToken = string.Empty;
        }

        [DllImport("__Internal")]
        private static extern void FarmverseGoogleSignIn(int id, NativeBridge.IosCallback callback);

        [DllImport("__Internal")]
        private static extern void FarmverseAppleSignIn(int id, string hashedNonce, NativeBridge.IosCallback callback);
#endif
    }
}
