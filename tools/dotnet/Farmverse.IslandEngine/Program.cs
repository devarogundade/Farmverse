using System;
using System.Collections.Generic;
using System.Text.Json;
using Farmverse.Domain.Gameplay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Farmverse.IslandEngine
{
    /// <summary>
    /// HTTP surface of the island engine (Cloud Run). Every call is pure: an island in, the island out.
    /// The backend owns storage, auth, scheduling and pushes; this only runs the rules.
    ///   GET  /healthz          liveness
    ///   GET  /v1/version       rules version
    ///   POST /v1/advance       { state, now } -> { state, report, nextWake }
    ///   POST /v1/raid          { state, now, salt } -> { state, raid, summary }   (the day's raid, player away)
    /// </summary>
    public static class Program
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            IncludeFields = true,        // the island save is plain public fields, like Unity's JsonUtility
            PropertyNamingPolicy = null, // keep the save's own names (they are already camelCase)
            WriteIndented = false,
        };

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.IncludeFields = true;
                o.SerializerOptions.PropertyNamingPolicy = null;
            });
            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
            builder.WebHost.UseUrls("http://0.0.0.0:" + port);
            var app = builder.Build();

            app.MapGet("/healthz", () => Results.Ok("ok"));
            app.MapGet("/v1/version", () => Results.Ok(new VersionResponse { rulesVersion = IslandSim.RulesVersion }));
            app.MapPost("/v1/advance", (AdvanceRequest req) => Engine.Advance(req));
            app.MapPost("/v1/raid", (RaidRequest req) => Engine.Raid(req));
            app.Run();
        }
    }

    /// <summary>The engine's calls, separate from HTTP so tests can run them directly.</summary>
    public static class Engine
    {
        public static IResult Advance(AdvanceRequest req)
        {
            if (req.state == null) return Results.BadRequest(new ErrorResponse { error = "state missing" });
            var now = req.now > 0 ? req.now : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (req.state.simAt > 0 && now < req.state.simAt)
                return Results.BadRequest(new ErrorResponse { error = "now is before the island's simAt" });
            try
            {
                var a = IslandSim.Advance(req.state, now);
                return Results.Ok(new AdvanceResponse
                {
                    state = a.State,
                    nextWake = a.NextWake,
                    report = ReportDto.Of(a.Report),
                    summary = new SummaryDto
                    {
                        netWorth = a.Economy.NetWorth,
                        level = a.Economy.IslandLevel,
                        xp = a.Economy.Xp,
                    },
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ErrorResponse { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        }

        public static IResult Raid(RaidRequest req)
        {
            if (req.state == null) return Results.BadRequest(new ErrorResponse { error = "state missing" });
            var now = req.now > 0 ? req.now : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (req.state.simAt > 0 && now < req.state.simAt)
                return Results.BadRequest(new ErrorResponse { error = "now is before the island's simAt" });
            try
            {
                var (a, raid) = IslandSim.Raid(req.state, now, req.salt);
                return Results.Ok(new RaidResponse
                {
                    state = a.State,
                    raid = raid,
                    summary = new SummaryDto { netWorth = a.Economy.NetWorth, level = a.Economy.IslandLevel, xp = a.Economy.Xp },
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new ErrorResponse { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        }
    }

    public sealed class RaidRequest
    {
        public IslandState? state;
        public double now;
        /// <summary>Makes each day's raid its own (the backend sends the day number).</summary>
        public long salt;
    }

    public sealed class RaidResponse
    {
        public IslandState state = new IslandState();
        public RaidOutcome raid = new RaidOutcome();
        public SummaryDto summary = new SummaryDto();
    }

    public sealed class AdvanceRequest
    {
        public IslandState? state;
        /// <summary>Server clock, seconds since 1970 (the backend sends it; absent = this machine's clock).</summary>
        public double now;
    }

    public sealed class AdvanceResponse
    {
        public IslandState state = new IslandState();
        public ReportDto report = new ReportDto();
        public double nextWake;
        /// <summary>What the backend shows of the island without running it (leaderboard, profile).</summary>
        public SummaryDto summary = new SummaryDto();
    }

    public sealed class SummaryDto
    {
        public long netWorth;
        public int level;
        public long xp;
    }

    public sealed class ReportDto
    {
        public double seconds;
        public int workers, loads, seedCoins;
        public bool storageFull;
        public Dictionary<string, int> gathered = new Dictionary<string, int>();
        // workers run the farm: what each job did, the food it took, and why work stopped
        public int harvested, replanted, fed, collected, refilled, restocked, sold, soldCoins, crates;
        public double foodUsed;
        public string stopped = "";
        public double stoppedAt;
        public bool capped;

        public static ReportDto Of(AwayReport r) => new ReportDto
        {
            seconds = r.Seconds, workers = r.Workers, loads = r.Loads, seedCoins = r.SeedCoins, storageFull = r.StorageFull,
            gathered = new Dictionary<string, int>(r.Gathered),
            harvested = r.Harvested, replanted = r.Replanted, fed = r.Fed, collected = r.Collected, refilled = r.Refilled,
            restocked = r.Restocked, sold = r.Sold, soldCoins = r.SoldCoins, crates = r.Crates,
            foodUsed = Math.Round(r.FoodUsed, 2), stopped = r.Stopped, stoppedAt = r.StoppedAt, capped = r.Capped,
        };
    }

    public sealed class VersionResponse
    {
        public int rulesVersion;
    }

    public sealed class ErrorResponse
    {
        public string error = "";
    }
}
