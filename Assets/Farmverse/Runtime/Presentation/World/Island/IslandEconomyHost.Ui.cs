#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>What the farmer is standing at (the game's UI shows that station's panel).</summary>
    public enum IslandStation { None, Unload, Harvest, Plant, Collect, Machine, Chop, Feed, Post }

    /// <summary>How a label over a building should look.</summary>
    public enum IslandLabelKind { Info, Progress, Ready, Alarm }

    /// <summary>One status label over a building (pads, fields, animal houses, machines, building sites).</summary>
    public struct IslandLabel
    {
        public int Uid;
        public Vector3 At;
        public string Text;
        public IslandLabelKind Kind;
        /// <summary>The good it's about (what's ready, what it needs), for its picture; null for none.</summary>
        public string? Item;
        /// <summary>0..1 progress (growing, building, making), or −1.</summary>
        public float Progress;
        /// <summary>0..1 health of a damaged building, or −1.</summary>
        public float Health;
    }

    /// <summary>
    /// The economy as the game's UI sees it: the station the farmer is at and what can be done there,
    /// the labels over buildings, the toast, the workers' report. Every action here is the same one the
    /// old panels performed, with the same sounds and messages.
    /// </summary>
    public sealed partial class IslandEconomyHost
    {
        public IslandStation Station => (IslandStation)(int)_work;
        public int StationSite => _workSite;

        /// <summary>The current message, while it's showing.</summary>
        public string? ToastNow => Time.unscaledTime < _toastUntil ? _toast : null;

        /// <summary>The farm workers' report waiting to be read (after time away), or null.</summary>
        public AwayReport? PendingReport => _report;
        public void DismissReport() => _report = null;

        /// <summary>Took goods out of this store here: the basket stays on your back while you stand in it.</summary>
        public bool KeepingBasketHere => _keepBasketAt >= 0 && _keepBasketAt == _workSite;

        public static string NameOf(string buildingId) => Name(buildingId);
        public static string PrettyOf(string item) => Cap(Pretty(item));
        public static string PeopleOf(DepartmentKind kind) => PeopleWord(kind);

        /// <summary>Sow the field the farmer stands in.</summary>
        public bool PlantHere(string crop)
        {
            if (_work != WorkKind.Plant || _workSite < 0) return false;
            if (Economy.Plant(_workSite, crop, Clock) == GatherResult.Done)
            {
                PlayerTool("Hoe");
                Motion()?.Play("Dig", 1.2f);
                IslandAudio.Play("sow", null, 0.7f);
                return true;
            }
            IslandAudio.Play("error", null, 0.6f);
            return false;
        }

        /// <summary>Take goods out of the store the farmer stands in, into the basket.</summary>
        public int TakeOutHere(string item, int count)
        {
            if (_workSite < 0 || !_sites.ContainsKey(_workSite)) return 0;
            var got = Economy.Withdraw(_workSite, item, count);
            if (got <= 0) { IslandAudio.Play("error", null, 0.5f); return 0; }
            _keepBasketAt = _workSite;
            for (var i = 0; i < Mathf.Min(got, 6); i++) Fly(item, _sites[_workSite].Building.transform.position + Vector3.up * (2f + i * 0.15f), BackPoint());
            IslandAudio.Play("pop", null, 0.6f, 0.03f, 0.05f);
            Toast($"Took {got} {Pretty(item)} — carry it where it's needed");
            return got;
        }

        /// <summary>Upgrade the department the farmer stands in (stock or energy, plus materials).</summary>
        public bool UpgradeDepartmentHere()
        {
            if (_workSite < 0 || !Economy.Departments.TryGetValue(_workSite, out var dept)) return false;
            if (!Economy.Upgrade(_workSite)) { IslandAudio.Play("error", null, 0.5f); return false; }
            Toast($"{Name(dept.BuildingId)} reached level {dept.Level}! (it eats a little more now)", 3f);
            IslandAudio.Play("level_up", null, 0.8f, 0f);
            if (Building(_workSite) is { } b) Magic.Pop(b.gameObject);
            return true;
        }

        /// <summary>Level up a machine or animal house: faster and roomier.</summary>
        public bool LevelUpBuilding(int uid)
        {
            if (!Economy.UpgradeBuilding(uid, Clock)) { IslandAudio.Play("error", null, 0.5f); return false; }
            Toast("Levelled up: faster, and holds more", 3f);
            IslandAudio.Play("level_up", null, 0.8f, 0f);
            if (Building(uid) is { } b) Magic.Pop(b.gameObject);
            return true;
        }

        /// <summary>Farm workers carry food over from the farmhouse to this house (or stop doing so).</summary>
        public void SendSupplies(int uid, bool on)
        {
            Economy.OrderSupplies(uid, on ? 200 : 0);
            IslandAudio.Play(on ? "pop" : "drop", null, 0.5f, 0f);
            Toast(on ? "Farm workers will bring food from the farmhouse" : "Supplies cancelled");
        }

        /// <summary>What each of your buildings is up to, as labels to float over them.</summary>
        public void Labels(List<IslandLabel> into)
        {
            into.Clear();
            if (_grid == null || !_begun) return;
            var now = Clock;
            foreach (var kv in _sites)
            {
                var b = kv.Value.Building;
                if (b == null) continue;
                var label = new IslandLabel { Uid = kv.Key, Progress = -1f, Health = -1f, Kind = IslandLabelKind.Info };
                var hp = Economy.Healths.TryGetValue(kv.Key, out var health) ? health : null;
                var mending = Economy.Sites.FirstOrDefault(c => c.Uid == kv.Key && c.Repair);
                if (hp != null && hp.Damaged && !hp.Ruined) label.Health = Mathf.Clamp01((float)(hp.Hp / hp.Max));
                if (hp != null && hp.Ruined)
                {
                    label.At = Centre(b) + Vector3.up * 3f;
                    label.Kind = IslandLabelKind.Alarm;
                    label.Text = mending == null ? "Ruined" : mending.Missing.Count > 0 ? $"Ruined · bring {MissingText(mending)}"
                        : Economy.Crews.ContainsKey(kv.Key) ? "Repairing" : "Ruined · waiting for a builder";
                    if (mending != null && mending.Missing.Count == 0 && Economy.Crews.ContainsKey(kv.Key)) label.Progress = (float)mending.Progress;
                    if (mending != null && mending.Missing.Count > 0) label.Item = mending.Missing.First().Key;
                }
                else if (mending != null && mending.Missing.Count > 0)
                {
                    label.At = Centre(b) + Vector3.up * 3f;
                    label.Kind = IslandLabelKind.Alarm;
                    label.Text = $"Damaged · needs {MissingText(mending)}";
                    label.Item = mending.Missing.First().Key;
                }
                else if (kv.Value.Pad != null && Economy.Departments.TryGetValue(kv.Key, out var d))
                {
                    label.At = Centre(b) + Vector3.up * 3.2f;
                    switch (d.Levels.Kind)
                    {
                        case DepartmentKind.Farmhouse:
                        case DepartmentKind.Silo:
                            label.Text = $"{Name(d.BuildingId)}  {d.Stored}/{d.StorageCapacity}";
                            label.Progress = d.StorageCapacity > 0 ? Mathf.Clamp01(d.Stored / (float)d.StorageCapacity) : -1f;
                            break;
                        case DepartmentKind.Market:
                            label.Text = Name(d.BuildingId) + " · sell here";
                            label.Item = "coins";
                            break;
                        default:
                            label.Text = d.People > 0 ? $"{Name(d.BuildingId)}  {d.Present}/{d.People}" : $"{Name(d.BuildingId)} Lv{d.Level}";
                            if (d.Starving) { label.Text += " · hungry"; label.Kind = IslandLabelKind.Alarm; label.Item = "croissant"; }
                            break;
                    }
                }
                else if (Economy.IsBuilt(kv.Key) && Economy.Plots.TryGetValue(kv.Key, out var plot))
                {
                    label.At = Centre(b) + Vector3.up * 1.6f;
                    var state = plot.State(now);
                    if (state == PlotState.Empty) { label.Text = "Empty field"; label.Item = "seed"; }
                    else
                    {
                        label.Item = plot.Crop;
                        if (state == PlotState.Ripe)
                        {
                            var rotting = plot.Rotting(now);
                            label.Kind = rotting ? IslandLabelKind.Alarm : IslandLabelKind.Ready;
                            label.Text = rotting ? $"Rotting ×{plot.Left}" : $"Ready ×{plot.Left}";
                            // a ripe field nobody will pick: say why, so it isn't a mystery
                            if (Economy.Has("workers_house") && !Economy.WorkOn(kv.Key, WorkJob.Harvest)) label.Text += " · workers off";
                        }
                        else
                        {
                            label.Kind = IslandLabelKind.Progress;
                            var left = Mathf.Max(0, Mathf.CeilToInt((float)(plot.ReadyAt - now)));
                            label.Text = left >= 60 ? $"{left / 60}m {left % 60}s" : $"{left}s";
                            var grow = plot.ReadyAt - plot.PlantedAt;
                            label.Progress = grow > 0 ? Mathf.Clamp01((float)((now - plot.PlantedAt) / grow)) : -1f;
                        }
                    }
                }
                else if (!Economy.IsBuilt(kv.Key))
                {
                    label.At = Centre(b) + Vector3.up * 2f;
                    var site = Economy.Sites.FirstOrDefault(c => c.Uid == kv.Key && !c.Repair);
                    if (site != null && site.Missing.Count > 0)
                    {
                        label.Kind = IslandLabelKind.Alarm;
                        label.Text = $"Needs {MissingText(site)}";
                        label.Item = site.Missing.First().Key;
                    }
                    else
                    {
                        label.Kind = IslandLabelKind.Progress;
                        label.Text = Economy.Crews.ContainsKey(kv.Key) ? "Building" : "Waiting for a builder";
                        label.Progress = (float)(site?.Progress ?? 0);
                    }
                }
                else if (Economy.Machines.TryGetValue(kv.Key, out var machine))
                {
                    label.At = Centre(b) + Vector3.up * 4f;
                    var missing = machine.Missing();
                    if (machine.Output > 0 && (machine.Output >= machine.OutputMax || !machine.Busy))
                    {
                        label.Kind = IslandLabelKind.Ready;
                        label.Item = machine.Recipe.Output;
                        label.Text = machine.Output >= machine.OutputMax ? $"×{machine.Output} full" : $"×{machine.Output}";
                    }
                    else if (machine.Busy)
                    {
                        label.Kind = IslandLabelKind.Progress;
                        label.Item = machine.Recipe.Output;
                        label.Text = machine.Output > 0 ? $"×{machine.Output} · making" : "Making";
                        label.Progress = (float)machine.Progress(now);
                    }
                    else if (missing != null)
                    {
                        label.Item = missing;
                        label.Text = $"Needs {Pretty(missing)}";
                    }
                    else continue;
                }
                else if (Economy.Animals.TryGetValue(kv.Key, out var house))
                {
                    label.At = Centre(b) + Vector3.up * 4f;
                    var n = house.StockAt(now);
                    if (n > 0) { label.Kind = IslandLabelKind.Ready; label.Item = house.Producer.Item; label.Text = $"×{n}"; }
                    else if (house.Hungry(now)) { label.Kind = IslandLabelKind.Alarm; label.Item = "feed"; label.Text = "Hungry · bring grain"; }
                    else
                    {
                        label.Kind = IslandLabelKind.Progress;
                        label.Item = house.Producer.Item;
                        var left = Mathf.Max(0, Mathf.CeilToInt((float)house.NextIn(now)));
                        label.Text = $"{left}s";
                        label.Progress = house.Interval > 0 ? Mathf.Clamp01(1f - (float)(house.NextIn(now) / house.Interval)) : -1f;
                    }
                    if (house.Side is { } side && side.StockAt(now) > 0) label.Text += $" · {Pretty(side.Producer.Item)} ×{side.StockAt(now)}";
                }
                else continue;
                into.Add(label);
            }
        }
    }
}
