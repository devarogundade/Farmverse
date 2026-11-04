#nullable enable

namespace Farmverse.Core.Lists
{

    /// <summary>How fresh a page must be.</summary>
    public enum Freshness
    {
        /// <summary>Whatever the source considers best (cache when offline, server otherwise).</summary>
        Default = 0,

        /// <summary>From the server; used by pull-to-refresh.</summary>
        Server = 1,

        /// <summary>From the local cache only.</summary>
        Cache = 2,
    }

}
