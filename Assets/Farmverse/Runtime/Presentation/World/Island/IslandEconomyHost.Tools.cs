#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Tools used on a building from wherever the camera is (dragged from its card, or sprayed across
    /// several): the economy does the work against the farmhouse store, this side shows it happening.
    /// </summary>
    public sealed partial class IslandEconomyHost
    {
        /// <summary>The tools a building takes right now (seeds on an empty field, the sickle on a ripe one…).</summary>
        public IReadOnlyList<Tool> ToolsFor(int uid) => Economy.ToolsFor(uid, Clock);

        public ToolRefusal CanUse(int uid, Tool tool) => Economy.CanUse(uid, tool, Clock);

        /// <summary>Uses a tool on a building: goods fly between it and the farmhouse, a sound plays, the island saves.</summary>
        public ToolUse UseTool(int uid, Tool tool)
        {
            var use = Economy.Use(uid, tool, Clock);
            if (!use.Ok) return use;
            var b = Building(uid);
            if (b != null)
            {
                var at = Centre(b) + Vector3.up * 1.2f;
                var house = NearestFarmhouse(at);
                var good = string.IsNullOrEmpty(use.Item) ? tool.Item : use.Item;
                if (house != null && !string.IsNullOrEmpty(good))
                {
                    var store = Centre(house) + Vector3.up * 1.5f;
                    var outward = tool.Kind == ToolKind.Sickle || tool.Kind == ToolKind.Hand;
                    var flights = Mathf.Clamp(use.Units, 1, 4);
                    for (var i = 0; i < flights; i++)
                    {
                        var jitter = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                        if (outward) Fly(good, at + jitter, store);
                        else Fly(good, store, at + jitter);
                    }
                }
                IslandAudio.Play(tool.Kind == ToolKind.Seed ? "pop" : "whoosh", b.transform.position, 0.6f, 0.05f, 0.2f, 1f);
            }
            Save();
            return use;
        }

        /// <summary>Plain words for why a tool didn't take.</summary>
        public static string RefusalText(ToolRefusal r, Tool tool) => r switch
        {
            ToolRefusal.WrongTarget => tool.Kind switch
            {
                ToolKind.Seed => "Seeds go on a field",
                ToolKind.Sickle => "The sickle cuts ripe fields",
                ToolKind.Feed => "Only animals eat that",
                ToolKind.Input => "That machine doesn't use " + Pretty(tool.Item),
                _ => "Nothing to collect there",
            },
            ToolRefusal.NotWorking => "It isn't working yet",
            ToolRefusal.NotReady => tool.Kind switch
            {
                ToolKind.Seed => "Something is already growing there",
                ToolKind.Sickle => "Not ripe yet",
                _ => "Nothing ready yet",
            },
            ToolRefusal.Locked => Pretty(tool.Item) + " unlocks at a higher level",
            ToolRefusal.NoCoins => "Not enough coins",
            ToolRefusal.Full => "Already full",
            ToolRefusal.NoStock => "No " + Pretty(tool.Item) + " in the farmhouse",
            ToolRefusal.StorageFull => "The farmhouse is full",
            _ => "",
        };

        /// <summary>A building just bought can be taken back for its full price while its builders haven't taken anything from the farmhouse.</summary>
        public bool CanUndoBuy(int uid)
        {
            var site = Economy.Sites.FirstOrDefault(c => c.Uid == uid);
            return site == null ? !(Economy.Plots.TryGetValue(uid, out var plot) && plot.Crop != null)
                                : !site.Supplied || site.Materials.Count == 0;
        }

        /// <summary>The good a tool shows as its picture: the seed's crop, the feed, the crop being cut, what's collected.</summary>
        public string ToolItem(int uid, Tool tool)
        {
            if (!string.IsNullOrEmpty(tool.Item)) return tool.Item;
            if (tool.Kind == ToolKind.Sickle) return Economy.Plots.TryGetValue(uid, out var plot) ? plot.Crop ?? "" : "";
            if (Economy.Animals.TryGetValue(uid, out var a)) return a.Producer.Item;
            if (Economy.Machines.TryGetValue(uid, out var m)) return m.Recipe?.Output ?? "";
            return "";
        }

        private PlacedBuilding? NearestFarmhouse(Vector3 from) =>
            _sites.Values.Select(s => s.Building)
                .Where(b => b != null && b.id == "farmhouse")
                .OrderBy(b => (Centre(b) - from).sqrMagnitude)
                .FirstOrDefault();
    }
}
