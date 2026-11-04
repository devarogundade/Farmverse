#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Infrastructure.Firebase;
using Farmverse.Infrastructure.Unity.Native;
using Firebase.Auth;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// Which link flow each provider uses on this platform (decision: Firebase
    /// Auth only, anonymous first):
    ///
    ///   Apple,  Android  Firebase OAuth flow (custom tab), no native step
    ///   Apple,  iOS      native AuthenticationServices token + nonce → credential
    ///   Google, Android  Credential Manager ID token → credential
    ///   Google, iOS      GoogleSignIn ID + access token → credential
    ///   editor/desktop   none: the buttons are hidden
    /// </summary>
    internal static class PlatformLinkFlows
    {
        public const string AppleProviderId = "apple.com";

        public static ILinkFlow[] Create()
        {
            var flows = new List<ILinkFlow>();
            if (NativeSignIn.GoogleAvailable)
                flows.Add(new CredentialLinkFlow(LinkProvider.Google, async c =>
                {
                    var tokens = await Native(NativeSignIn.Google, c);
                    return GoogleAuthProvider.GetCredential(tokens.IdToken, tokens.AccessToken);
                }));
            if (NativeSignIn.AppleTokenAvailable)
                flows.Add(new CredentialLinkFlow(LinkProvider.Apple, async c =>
                {
                    var tokens = await Native(NativeSignIn.Apple, c);
                    return OAuthProvider.GetCredential(AppleProviderId, tokens.IdToken, tokens.RawNonce, null);
                }));
#if UNITY_ANDROID && !UNITY_EDITOR
            else flows.Add(new FederatedLinkFlow(LinkProvider.Apple, AppleProviderId));
#endif
            return flows.ToArray();
        }

        /// <summary>A native step's failure as the error the player sees.</summary>
        private static async Task<NativeTokens> Native(Func<CancellationToken, Task<NativeTokens>> step, CancellationToken cancellation)
        {
            try
            {
                return await step(cancellation);
            }
            catch (NativeBridgeException error)
            {
                var code = error.Code switch
                {
                    NativeBridgeException.Cancelled => ErrorCode.Cancelled,
                    NativeBridgeException.Unsupported => ErrorCode.Unsupported,
                    NativeBridgeException.Unavailable => ErrorCode.Unsupported,
                    _ => ErrorCode.NetworkRequired,
                };
                throw new LinkFlowException(code, error.Message);
            }
        }
    }
}
