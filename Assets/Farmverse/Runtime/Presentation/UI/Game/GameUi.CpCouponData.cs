#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Contracts;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// A coupon type as the screens use it (built from the catalogue's couponTypes). The app knows only this generic shape: name, logo, colours,
    /// unit, redeem link, redeem steps and expiry all come from data (B3TR is just the first type).
    /// </summary>
    [Serializable]
    public sealed class CpCouponType
    {
        public string id = "", name = "";
        /// <summary>Pack picture used as the logo until logos are downloaded from logoUrl; else <see cref="logoGlyph"/>.</summary>
        public string logoArt = "", logoGlyph = "", logoUrl = "";
        public string logoBg = "#ffffff", accentA = "#9be15d", accentB = "#2fbf71", ink = "#0b3a1a";
        public string unit = "", redeemUrl = "", redeemLabel = "Open redeem page";
        /// <summary>Redeem steps; &lt;b&gt;…&lt;/b&gt; marks the words shown in gold.</summary>
        public string[] steps = Array.Empty<string>();
        public int expiryDays = 30;
    }

    /// <summary>A daily task or goal that pays a coupon of a type.</summary>
    public sealed class CpCouponTask
    {
        public string Id = "", Name = "", Art = "", TypeId = "";
        public bool Daily;
        public int Of = 1, Quantity = 1, LockLevel;
        /// <summary>What its coupon is worth, in the type's unit.</summary>
        public double Amount;
        /// <summary>Progress so far (daily tasks: since today began).</summary>
        public Func<IslandEconomy?, int> Progress = _ => 0;

        public string Label => (Daily ? "Daily · " : "Goal · ") + Name;
    }

    /// <summary>An issued coupon code.</summary>
    [Serializable]
    public sealed class CpCoupon
    {
        public string code = "", typeId = "", taskId = "", task = "";
        public double issuedAt, expiresAt, copiedAt;
        public int day;
        /// <summary>What it is worth, in its type's unit.</summary>
        public double amount;
    }

    public static class CpAmount
    {
        /// <summary>"10 B3TR", "0.5 USDT".</summary>
        public static string Of(double amount, CpCouponType type) =>
            amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + (type.unit.Length > 0 ? type.unit : type.name);
    }

    /// <summary>
    /// Coupons as the server runs them: types and tasks from the catalogue (<c>couponTypes</c>,
    /// <c>couponTasks</c>), codes made by the server, each worth its task's amount (<see cref="GameUi.Coupons"/>).
    /// Task progress is shown from the island here; the server checks it again on its copy when a code is asked for.
    /// Only "copied" is remembered on the device (it changes nothing).
    /// </summary>
    public static class CpCouponStore
    {
        private const string CopiedPref = "fv_coupon_copied_";

        public static List<CpCouponType> Types
        {
            get
            {
                var list = new List<CpCouponType>();
                foreach (var kv in Wallet.Prices.CouponTypes)
                {
                    var t = kv.Value;
                    list.Add(new CpCouponType
                    {
                        id = kv.Key, name = t.Name, logoArt = t.LogoArt, logoGlyph = t.LogoGlyph, logoBg = t.LogoBg, accentA = t.AccentA, accentB = t.AccentB,
                        ink = t.Ink, unit = t.Unit, redeemUrl = t.RedeemUrl, redeemLabel = t.RedeemLabel, steps = t.Steps.ToArray(), expiryDays = t.ExpiryDays,
                    });
                }
                return list;
            }
        }

        public static List<CpCouponTask> Tasks
        {
            get
            {
                var list = new List<CpCouponTask>();
                foreach (var kv in Wallet.Prices.CouponTasks.OrderBy(kv => kv.Value.Order).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var t = kv.Value;
                    var progress = t.Progress;
                    list.Add(new CpCouponTask
                    {
                        Id = kv.Key, Name = t.Name, Art = t.Art, TypeId = t.TypeId, Daily = t.Daily, Of = t.Of, LockLevel = t.LockLevel, Amount = t.Amount,
                        Progress = e => ProgressOf(progress, e),
                    });
                }
                return list;
            }
        }

        /// <summary>The same reading of a task's progress the server makes (CouponTask.progress).</summary>
        private static int ProgressOf(string progress, IslandEconomy? e)
        {
            if (e == null) return 0;
            if (progress.StartsWith("today:", StringComparison.Ordinal)) return e.Today(progress.Substring(6));
            if (progress == "goals") return e.GoalIndex;
            if (progress == "level") return e.BestLevel;
            return e.Stat(progress);
        }

        public static CpCouponType Type(string id)
        {
            var all = Types;
            return all.Find(t => t.id == id) ?? (all.Count > 0 ? all[0] : new CpCouponType { id = id, name = id });
        }

        /// <summary>Every code the server issued, newest first.</summary>
        public static IReadOnlyList<CpCoupon> Codes
        {
            get
            {
                var l = new List<CpCoupon>();
                var svc = GameUi.Coupons;
                if (svc == null) return l;
                foreach (var (_, d) in svc.Codes) l.Add(ToView(d));
                l.Sort((a, b) => b.issuedAt.CompareTo(a.issuedAt));
                return l;
            }
        }

        private static CpCoupon ToView(CouponDoc d) => new CpCoupon
        {
            code = d.Code, typeId = d.TypeId, taskId = d.TaskId, task = d.Task,
            issuedAt = d.IssuedAt / 1000.0, expiresAt = d.ExpiresAt / 1000.0, amount = d.Amount,
            copiedAt = PlayerPrefs.GetInt(CopiedPref + d.Code, 0) == 1 ? 1 : 0,
        };

        public static CpCoupon? Find(string code)
        {
            foreach (var c in Codes)
                if (c.code == code) return c;
            return null;
        }

        private static string UtcDay => DateTimeOffset.FromUnixTimeSeconds((long)Farmverse.Presentation.World.Island.IslandEconomyHost.Clock).UtcDateTime
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The code this task already paid (today, for daily tasks).</summary>
        public static CpCoupon? CodeFor(CpCouponTask t)
        {
            var svc = GameUi.Coupons;
            if (svc == null) return null;
            var id = t.Id + "_" + (t.Daily ? UtcDay : "once");
            foreach (var (docId, d) in svc.Codes)
                if (docId == id) return ToView(d);
            return null;
        }

        public enum State { InProgress, Ready, Done, Locked }

        public static State StateOf(CpCouponTask t, IslandEconomy? e)
        {
            if (t.LockLevel > 0 && (e?.BestLevel ?? 1) < t.LockLevel) return State.Locked;
            if (CodeFor(t) != null) return State.Done;
            return t.Progress(e) >= t.Of ? State.Ready : State.InProgress;
        }

        public static int ReadyCount(IslandEconomy? e)
        {
            var n = 0;
            foreach (var t in Tasks)
                if (StateOf(t, e) == State.Ready) n++;
            return n;
        }

        public static void MarkCopied(CpCoupon c)
        {
            if (c.copiedAt > 0) return;
            c.copiedAt = 1;
            PlayerPrefs.SetInt(CopiedPref + c.code, 1);
            PlayerPrefs.Save();
        }
    }
}
