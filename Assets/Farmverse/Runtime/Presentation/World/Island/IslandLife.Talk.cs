#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Talking to the island's people. Walk up to a farm worker, builder or warrior and stop: they
    /// stop too, turn to you and wave, and you can ask them things their job lets them know. A farm
    /// worker knows the stores and the fields, a builder the sites and the repairs, a warrior the
    /// raids. Every answer is read off the island as it is right now, and everyone can tell you what
    /// they think needs doing most. Walk away (or say bye) and they go back to work.
    /// </summary>
    public sealed partial class IslandLife
    {
        private Agent? _talking;
        private Agent? _talkedTo;          // the last one, so they don't stop you again until you've moved on
        private string _answer = "";
        private string _lastRaid = "";
        private Vector3 _farmerWas;
        private float _farmerStill;
        private bool _talkArmed = true;
        private float _talkSince;

        private static readonly HashSet<string> NotFood = new HashSet<string>
        {
            "wood", "stone", "planks", "feed", "oil",
        };

        /// <summary>Stop for whoever the farmer has walked up to and stopped beside.</summary>
        private void TalkCheck()
        {
            var player = _host!.Player;
            if (player == null || player.Riding || IslandChapters.AnyGrowing) { EndTalk(); return; }
            var at = player.transform.position;
            var moved = Flat(at - _farmerWas).magnitude;
            _farmerWas = at;
            _farmerStill = moved < 0.02f ? _farmerStill + Time.deltaTime : 0f;
            if (moved >= 0.02f) _talkArmed = true;   // one chat per stop: walk on before anyone else greets you
            if (_talking != null)
            {
                if (_talking.Gone || _talking.Job == Job.Down || Flat(_talking.Body.position - at).magnitude > 3.2f) EndTalk();
                return;
            }
            if (_talkedTo != null && (_talkedTo.Gone || Flat(_talkedTo.Body.position - at).magnitude > 4f)) _talkedTo = null;
            // only when you stop beside them facing them, not while you're busy at something, and not mid-raid
            if (_farmerStill < 0.5f || !_talkArmed || _raidLanded || _host.FarmerBusy) return;
            var facing = player.transform.forward;
            var who = _agents.Where(a => a.Role != Role.Raider && !a.Gone && a.Body != null && a != _talkedTo
                                         && a.Job is not (Job.Down or Job.Leave or Job.Fight or Job.Hide)
                                         && Flat(a.Body.position - at).magnitude < 2.2f
                                         && Vector3.Dot(Flat(a.Body.position - at).normalized, Flat(facing).normalized) > 0.3f)
                .OrderBy(a => Flat(a.Body.position - at).sqrMagnitude).FirstOrDefault();
            if (who == null) return;
            _talking = who;
            _talkSince = Time.time;
            _talkedTo = who;
            _talkArmed = false;
            _answer = Greeting(who);
            who.Motion?.Release();
            who.Motion?.Play("Wave", 1.6f);
            IslandAudio.Play("pop", who.Body.position, 0.4f, 0.1f, 0.5f, 1.4f);
        }

        /// <summary>While talking they stand still and look at you; their job waits.</summary>
        private void TalkStep(Agent a)
        {
            var player = _host!.Player;
            if (player != null) Face(a, player.transform.position);
            // chatting: the talk gestures, turn about (Santa's talk and listen clips)
            if (Time.time - _talkSince > 1.6f) a.Motion?.Hold(((int)((Time.time - _talkSince) / 3.5f)) % 2 == 0 ? "Talk" : "Listen");
        }

        private void EndTalk()
        {
            if (_talking == null) return;
            _talking.Motion?.Release();
            _talking.Repath = 0f;
            _talking = null;
        }

        // ---- the conversation ------------------------------------------------------------------

        private string Greeting(Agent a) => a.Role switch
        {
            Role.Worker => "Hello, boss! Ask me about the stores or the fields.",
            Role.Builder => "Hi there! Want to know how the building's going?",
            Role.Warrior => "On guard! Ask me about the zombies.",
            Role.Villager => a.Kind.Contains("grandma") ? "Ah, hello dear. Lovely island you're making." : a.Kind.Contains("kid") ? "Hi! Hi! Did you see the chickens?" : a.Kind.Contains("baker") ? "Morning! Bread's better with flour from your own mill." : "Good day to you!",
            _ => "Hello!",
        };

        private IEnumerable<(string q, System.Func<Agent, string> a)> Questions(Agent who) => who.Role switch
        {
            Role.Worker => new (string, System.Func<Agent, string>)[]
            {
                ("How much food is in the store?", _ => FoodAnswer()),
                ("Wood and stone?", _ => MaterialsAnswer()),
                ("What are you doing?", JobAnswer),
                ("How is our house?", HouseAnswer),
                ("What needs doing?", _ => Advice()),
            },
            Role.Builder => new (string, System.Func<Agent, string>)[]
            {
                ("What are you building?", _ => SitesAnswer()),
                ("Is anything missing?", _ => MissingAnswer()),
                ("Wood and stone?", _ => MaterialsAnswer()),
                ("Anything damaged?", _ => DamageAnswer()),
                ("What needs doing?", _ => Advice()),
            },
            Role.Villager => new (string, System.Func<Agent, string>)[]
            {
                ("What's new?", _ => NewsAnswer()),
                ("How's the island?", _ => IslandAnswer()),
                ("What's that building?", BuildingAnswer),
                ("What's the weather?", _ => WeatherAnswer()),
                ("Any tips?", _ => Advice()),
            },
            _ => new (string, System.Func<Agent, string>)[]
            {
                ("Are we ready for the zombies?", _ => ReadyAnswer()),
                ("When will they come?", _ => WhenAnswer()),
                ("How is the army?", HouseAnswer),
                ("How did the last raid go?", _ => _lastRaid.Length > 0 ? _lastRaid : "No raid since I got here. Long may it last."),
                ("What needs doing?", _ => Advice()),
            },
        };

        /// <summary>The game's language for text made here from pieces (set by the UI; English until then).</summary>
        public static System.Func<string, string> Words { get; set; } = s => s;

        private string NewsAnswer()
        {
            var news = _host!.Log.Take(2).Select(l => l.text).ToList();
            // the pieces are translated one by one (the news lines are the game's own messages)
            return news.Count == 0 ? "Quiet as a mouse round here." : Words("Did you hear?") + " " + string.Join(" " + Words("And:") + " ", news.Select(Words));
        }

        private string IslandAnswer()
        {
            var now = IslandEconomyHost.Clock;
            var (made, eaten) = Eco.Flow(now);
            var mood = made >= eaten ? "Plenty to eat, too." : "Though we eat more than we grow, mind.";
            return $"Island level {Eco.BestLevel}, {_host!.Buildings.Count()} buildings and {_agents.Count(x => x.Role != Role.Raider)} of us folk. {mood}";
        }

        private string BuildingAnswer(Agent a)
        {
            var host = _host!;
            var near = host.Buildings.Select(u => host.Building(u)).Where(b => b != null)
                .OrderBy(b => Flat(host.CentreOf(b!) - a.Body.position).sqrMagnitude).FirstOrDefault();
            if (near == null) return "Which one? There's nothing here yet!";
            var what = GameplayRules.Describe(near.id);
            return $"That's the {Pretty(near.id)}. {(what.Length > 0 ? what + "." : "")}";
        }

        private string WeatherAnswer()
        {
            var now = IslandEconomyHost.Clock;
            var s = Eco.SeasonNow(now);
            return $"It's {s.Name.ToLowerInvariant()}: {s.Hint.ToLowerInvariant()}. {GameplayRules.WeatherText(GameplayRules.WeatherAt(now))}.";
        }

        private Department? Farmhouse => Eco.Departments.Values.FirstOrDefault(d => d.Levels.Kind == DepartmentKind.Farmhouse && Eco.Working(d.Uid));

        private int Stored(string item) => Eco.Departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && Eco.Working(d.Uid))
            .Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);

        private string FoodAnswer()
        {
            var food = Eco.Departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && Eco.Working(d.Uid))
                .SelectMany(d => d.Stock).Where(kv => kv.Value > 0 && !NotFood.Contains(kv.Key))
                .GroupBy(kv => kv.Key).Select(g => (item: g.Key, n: g.Sum(kv => kv.Value))).OrderByDescending(x => x.n).ToList();
            var house = Farmhouse;
            if (food.Count == 0) return "The store has no food at all! Bring in a harvest, or eggs from the coop.";
            var total = food.Sum(f => f.n);
            var energy = food.Sum(f => f.n * GameplayRules.EnergyOf(f.item));
            var list = string.Join(", ", food.Take(4).Select(f => $"{f.n} {Pretty(f.item)}"));
            var room = house != null ? $" Room for {house.StorageCapacity - house.Stored} more." : "";
            return $"{total} food ({energy} energy): {list}{(food.Count > 4 ? " and more" : "")}.{room}";
        }

        private string MaterialsAnswer()
        {
            var wood = Stored("wood"); var stone = Stored("stone"); var planks = Stored("planks");
            var tip = wood < 10 ? " We're low on wood: chop some trees." : stone < 5 ? " Stone's short: break up some rocks." : "";
            return $"{wood} wood, {stone} stone, {planks} planks in the store.{tip}";
        }

        private string JobAnswer(Agent a)
        {
            string Name(int uid) => _host!.Building(uid) is { } b ? Pretty(b.id) : "that";
            return a.Job switch
            {
                Job.Harvest => $"Bringing in the {Name(a.Target)} crop.",
                Job.Plant => "Sowing a bare field.",
                Job.Collect => $"Collecting from the {Name(a.Target)}.",
                Job.Deliver => a.Load.Count > 0 ? $"Carrying {a.Load.Count} {Pretty(a.Load[0])} to the farmhouse." : "Off to the farmhouse.",
                Job.Fetch => $"Fetching supplies for the {Name(a.Target2)}.",
                Job.Feed => $"Taking feed to the {Name(a.Target2)}.",
                Job.Supply => $"Taking this load to the {Name(a.Target2)}.",
                Job.Rest => "Waiting. Our house has no food, and I can't work hungry.",
                Job.Tired => "Catching my breath. That was a lot of loads!",
                Job.Chop => "Getting wood and stone for the farmhouse. The builders need it.",
                Job.Build => $"Building the {Name(a.Target)}.",
                _ => a.Role == Role.Worker ? "Nothing to do right now. More fields or animals would keep me busy." : "Looking around.",
            };
        }

        private string HouseAnswer(Agent a)
        {
            if (!Eco.Departments.TryGetValue(a.Home, out var d)) return "I've no home to speak of.";
            var mood = d.Starving ? "We're starving! Bring food fast or people will leave." : d.Energy < d.UpkeepPerMinute * 5 ? "Food's running low." : "We're fed and fine.";
            var armed = d.Levels.Kind == DepartmentKind.ArmyHouse ? (d.Armed ? " Everyone has iron, so we hit hard." : " Iron would make us hit harder.") : "";
            return $"We're {d.Present} of {d.People}, level {d.Level}, with {Mathf.FloorToInt((float)d.Energy)} energy (we eat {d.UpkeepPerMinute:0.#} a minute). {mood}{armed}";
        }

        private string SitesAnswer()
        {
            var sites = Eco.Sites.ToList();
            if (sites.Count == 0) return "Nothing on the go. Buy something and I'll build it!";
            var lines = sites.Take(3).Select(c => $"the {(_host!.Building(c.Uid) is { } b ? Pretty(b.id) : "site")} {(c.Repair ? "repair" : "")} ({c.Progress:P0})".Replace("  ", " "));
            return $"Working on {string.Join(", ", lines)}{(sites.Count > 3 ? $" and {sites.Count - 3} more" : "")}.";
        }

        private string MissingAnswer()
        {
            var waiting = Eco.Sites.Where(c => c.Missing.Count > 0).ToList();
            if (waiting.Count == 0) return Eco.Sites.Count > 0 ? "We've got everything we need." : "Nothing's waiting on anything.";
            var c0 = waiting[0];
            var what = string.Join(", ", c0.Missing.Select(m => $"{m.Value} {Pretty(m.Key)}"));
            var where = c0.Missing.ContainsKey("planks") ? " A lumber yard makes planks." : c0.Missing.ContainsKey("stone") ? " Break rocks for stone." : " Chop trees for wood.";
            return $"The {(_host!.Building(c0.Uid) is { } b ? Pretty(b.id) : "site")} needs {what} in the farmhouse.{where}";
        }

        private string DamageAnswer()
        {
            var hurt = Eco.Healths.Where(kv => kv.Value.Damaged || kv.Value.Ruined).ToList();
            if (hurt.Count == 0) return "Not a scratch on anything.";
            var ruined = hurt.Count(kv => kv.Value.Ruined);
            var first = hurt.OrderByDescending(kv => kv.Value.Ruined).First();
            var name = _host!.Building(first.Key) is { } b ? Pretty(b.id) : "a building";
            return $"{hurt.Count} damaged{(ruined > 0 ? $", {ruined} ruined" : "")}. Worst is the {name}. We fix them with wood (a fire station mends them for free).";
        }

        private string ReadyAnswer()
        {
            var threat = Eco.Threat; var defence = Eco.Defence;
            var size = GameplayRules.RaidSize(Eco.BestLevel, threat, Eco.Stat("raids_seen"));
            var verdict = defence >= size * 1.5 ? "We'll see them off." : defence >= size * 0.7 ? "It'll be a fair fight." : defence < 0.5 ? "There's no one to fight them! Build an army house." : "They'd beat us. We need more warriors, food and iron.";
            return $"We're {defence:0.#} strong. The next zombie night will bring about {size} zombie{(size == 1 ? "" : "s")}. {verdict}";
        }

        private string WhenAnswer()
        {
            if (_raidOn) return "They're here!";
            // one raid a day
            if (RaidSchedule.DoneToday) return "Not again today. They come once a day — be ready tomorrow.";
            var mins = Mathf.CeilToInt(NextRaidIn / 60f);
            return mins <= 1 ? "Any moment now. Get ready!" : $"In about {mins} minutes. They come once a day.";
        }

        /// <summary>What matters most on the island right now, by urgency.</summary>
        private string Advice()
        {
            var now = IslandEconomyHost.Clock;
            var eco = Eco;
            var starving = eco.Departments.Values.FirstOrDefault(d => eco.Working(d.Uid) && d.Starving && d.People > 0);
            if (starving != null) return $"The {Pretty(starving.BuildingId)} is starving. Walk food into it, or order supplies from the farmhouse.";
            var rotting = eco.Plots.Values.Count(p => p.Rotting(now));
            if (rotting > 0) return $"{rotting} field{(rotting == 1 ? " is" : "s are")} rotting! Harvest now, or get more farm workers.";
            var hungry = eco.Animals.Values.FirstOrDefault(h => eco.Working(h.Uid) && h.Hungry(now));
            if (hungry != null) return $"The {Pretty(hungry.BuildingId)} animals are hungry. Bring them grain.";
            if (NextRaidIn < 120 && eco.Defence < GameplayRules.RaidSize(eco.BestLevel, eco.Threat, eco.Stat("raids_seen"))) return "Zombies are coming soon, and we're too few to stop them. Feed the army or build up the guard.";
            var waiting = eco.Sites.FirstOrDefault(c => c.Missing.Count > 0);
            if (waiting != null) return MissingAnswer();
            var house = Farmhouse;
            if (house != null && house.Stored >= house.StorageCapacity * 0.9) return "The farmhouse is nearly full. Sell at the market or upgrade it.";
            var ripe = eco.Plots.Values.Count(p => p.State(now) == PlotState.Ripe);
            if (ripe > 0) return $"{ripe} field{(ripe == 1 ? " is" : "s are")} ready to harvest.";
            if (eco.Order is { } order) return $"The post office wants {order.Left} more {Pretty(order.Item)}: better pay than the market.";
            if (!eco.Departments.Values.Any(d => d.Levels.Kind == DepartmentKind.WorkersHouse)) return "A workers' house would give you hands to farm while you're away.";
            if (!eco.Departments.Values.Any(d => d.Levels.Kind == DepartmentKind.ArmyHouse)) return "We've no army. Zombies will come once the island's worth robbing.";
            return "All's well! Another field or a new building would grow the island.";
        }

        // ---- the panel -----------------------------------------------------------------------------

    }
}
