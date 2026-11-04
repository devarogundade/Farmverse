#nullable enable
using System;

namespace Farmverse.Contracts
{
    /// <summary>
    /// A wire value did not match its contract: a missing field, a wrong type, a
    /// fractional number where an integer was promised.
    ///
    /// This is a defect (client and server disagree about the contract), never a
    /// player-facing condition, so it is an exception rather than a result.
    /// </summary>
    public sealed class WireFormatException : Exception
    {
        public WireFormatException(string path, string problem)
            : base(path + ": " + problem)
        {
            Path = path;
            Problem = problem;
        }

        /// <summary>Where the problem is, e.g. <c>FarmingPlantResult.slot</c>.</summary>
        public string Path { get; }

        /// <summary>What is wrong with the value.</summary>
        public string Problem { get; }
    }
}
