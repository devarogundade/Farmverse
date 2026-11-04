#nullable enable
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity
{
    public sealed class UnityAppInfo : IAppInfo
    {
        public string Version => UnityEngine.Application.version;

        public Platform Platform
        {
            get
            {
                switch (UnityEngine.Application.platform)
                {
                    case RuntimePlatform.Android: return Platform.Android;
                    case RuntimePlatform.IPhonePlayer: return Platform.Ios;
                    default: return Platform.Editor;
                }
            }
        }

        public string Locale => System.Globalization.CultureInfo.CurrentUICulture.Name;
    }
}
