#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>The island's people as the game's UI sees them: the conversation, the raid, the hurt fighters.</summary>
    public sealed partial class IslandLife
    {
        // ---- talking -------------------------------------------------------------------------------

        public bool Talking => _talking != null && !_talking.Gone;

        /// <summary>Who you're talking to ("Farm worker", "Grandma"…).</summary>
        public string TalkName => _talking == null ? "" : TitleOf(_talking);

        /// <summary>A key for their picture: worker, builder, warrior, guard, grandma, kid, baker, villager.</summary>
        public string TalkWho => _talking == null ? "" : _talking.Role switch
        {
            Role.Worker => "worker",
            Role.Builder => "builder",
            Role.Warrior => TitleOf(_talking) == "Guard" ? "guard" : "warrior",
            Role.Villager => _talking.Kind.Contains("grandma") ? "grandma" : _talking.Kind.Contains("kid") ? "kid" : _talking.Kind.Contains("baker") ? "baker" : "villager",
            _ => "villager",
        };

        public string TalkAnswer => _answer;

        public IReadOnlyList<string> TalkQuestions => _talking == null ? new string[0] : Questions(_talking).Select(q => q.q).ToList();

        /// <summary>Ask the question at <paramref name="index"/>; the answer replaces <see cref="TalkAnswer"/>.</summary>
        public void Ask(int index)
        {
            var who = _talking;
            if (who == null) return;
            var questions = Questions(who).ToList();
            if (index < 0 || index >= questions.Count) return;
            _answer = questions[index].a(who);
            IslandAudio.Play("pop", who.Body.position, 0.35f, 0.1f, 0.1f, 1.3f);
            if (index == questions.Count - 1) who.Motion?.Play("LookAround", 1.5f);
        }

        public void SayBye() => EndTalk();

        private string TitleOf(Agent who) => who.Role switch
        {
            Role.Worker => "Farm worker",
            Role.Builder => "Builder",
            Role.Villager => who.Kind.Contains("grandma") ? "Grandma" : who.Kind.Contains("kid") ? "Kid" : who.Kind.Contains("baker") ? "Baker" : "Villager",
            Role.Warrior => Eco.Departments.TryGetValue(who.Home, out var d) && d.Levels.Kind == DepartmentKind.Watch ? "Guard" : "Warrior",
            _ => "Islander",
        };

        // ---- the raid ------------------------------------------------------------------------------

        /// <summary>The raiders have come ashore (before that they're on their way).</summary>
        public bool RaidLanded => _raidOn && _raidLanded;

        /// <summary>Which side of the island they come from ("north", "south-east"…).</summary>
        public string RaidFrom => _raidFrom;

        /// <summary>Seconds until they land (0 once they have).</summary>
        public float RaidLandsIn => _raidOn && !_raidLanded ? Mathf.Max(0f, _landAt - _playTime) : 0f;

        public int RaidersLeft => _agents.Count(a => a.Role == Role.Raider && a.Job != Job.Down && !a.Gone);

        /// <summary>The raider nearest the farmer (or where they'll land): what Look and Ride go to.</summary>
        public Vector3 RaidFocus
        {
            get
            {
                var from = _host?.Player != null ? _host.Player.transform.position : _raidAt;
                var raiders = _agents.Where(a => a.Role == Role.Raider && a.Job != Job.Down && !a.Gone && a.Body != null).ToList();
                return raiders.Count > 0 ? raiders.OrderBy(r => Flat(r.Body.position - from).sqrMagnitude).First().Body.position : _raidAt;
            }
        }

        public void LookAtRaid() => LookRequested?.Invoke(RaidFocus);
        public void RideToRaid() => RideRequested?.Invoke(RaidFocus);

        /// <summary>Warriors and raiders who are hurt: where (over their heads), how healthy, and whose side.</summary>
        public void Hurt(List<(Vector3 at, float health, bool raider)> into)
        {
            into.Clear();
            foreach (var a in _agents)
            {
                if (a.Body == null || a.Gone || (a.Role != Role.Warrior && a.Role != Role.Raider) || a.Hp >= a.MaxHp || a.Job == Job.Down) continue;
                into.Add((a.Body.position + Vector3.up * 3f, Mathf.Clamp01(a.Hp / a.MaxHp), a.Role == Role.Raider));
            }
        }
    }
}
