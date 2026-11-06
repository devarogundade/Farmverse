#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Farmverse.Application.Ports
{
    /// <summary>
    /// The player's NEAR wallet, for the few things the game signs on chain (mint, redeem; docs 13 §7).
    /// On the web the page's wallet asks the player to approve; elsewhere there is none.
    /// </summary>
    public interface IWallet
    {
        /// <summary>Whether a wallet can sign here (the web build).</summary>
        bool Available { get; }

        /// <summary>
        /// Asks the player to approve one function call; returns its transaction hash once it succeeded.
        /// Throws <see cref="WalletException"/> when the player declines or the call fails.
        /// </summary>
        Task<string> Call(string contractId, string method, IReadOnlyDictionary<string, object> args, string depositYocto, int gasTgas);
    }

    public sealed class WalletException : System.Exception
    {
        public WalletException(string message, bool declined) : base(message) => Declined = declined;

        /// <summary>The player closed the wallet or rejected the request.</summary>
        public bool Declined { get; }
    }
}
