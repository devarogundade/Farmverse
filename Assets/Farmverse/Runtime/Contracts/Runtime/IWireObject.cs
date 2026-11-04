#nullable enable
using System.Collections.Generic;

namespace Farmverse.Contracts
{
    /// <summary>
    /// A contract type that converts itself to and from the wire form the Firebase
    /// SDKs speak (<c>Dictionary&lt;string, object&gt;</c>, lists and primitives).
    /// Implemented by every generated type; never by hand.
    /// </summary>
    public interface IWireObject
    {
        Dictionary<string, object> ToWire();
    }
}
