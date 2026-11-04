#nullable enable
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The island growing with its chapter (docs: Island by Chapter). When the island's level reaches a new
    /// chapter (11, 26) and nothing else is on screen, a chapter card says what the new land brings; then
    /// the growth plays (the land rises, the camera tours the new places) under a see-through overlay that
    /// names each place and skips on a tap. If the level was reached away from the island, this plays the
    /// next time it opens.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegChapter = Register("chapter", UiLayer.Popup, (ui, c) => ui.ChapterCard(c), .5f);
        private static readonly bool RegGrowTour = Register("growtour", UiLayer.Popup, (ui, c) => ui.GrowTour(c));

        /// <summary>From the play screen's live update: offer the growth when it's due, close the overlay when it's done.</summary>
        private void ChapterTick()
        {
            var sb = Sandbox;
            var eco = Economy;
            var chapters = sb != null ? sb.Chapters : null;
            if (sb == null || eco == null || chapters == null || !sb.Running) return;
            if (TopPopup == "growtour" && !chapters.Growing) { Back(); return; }
            if (sb.Visiting || chapters.Growing || TopPopup != null) return;
            var due = Mathf.Min(GameplayRules.IslandChapterFor(eco.BestLevel), chapters.Chapters);
            if (due > sb.IslandChapter) Open("chapter", due);
        }

        private void ChapterCard(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var to = c.Args is int a ? a : (Sandbox?.IslandChapter ?? 1) + 1;
            var firstLevel = to == 2 ? GameplayRules.ChapterEnds[0] + 1 : GameplayRules.ChapterEnds[1] + 1;

            var bg = PK.Inset(PK.Box(c.Root));
            bg.pickingMode = PickingMode.Position;
            bg.style.backgroundColor = GameTheme.T("#062050", .92f);
            var glow = PK.Inset(PK.Box(bg));
            Kit.Picture(glow, GameArt.Radial("ch_glow_" + GameTheme.Current, .5f, .4f, .6f, .55f,
                (0f, GameTheme.T("#9cffc9", .8f)), (.45f, GameTheme.T("#27b88a", .35f)), (.75f, GameTheme.T("#062050", .95f)), (1f, GameTheme.T("#062050", .95f))));
            var rs = p.W * 2f;
            var rays = PK.Abs(PK.Box(bg), left: (p.W - rs) / 2f, top: p.H * .4f - rs / 2f, width: rs, height: rs);
            Kit.Picture(rays, CpArt.Rays());
            CpSpin(c, rays, 24f);

            var col = PK.Col(c.Root, P(18));
            col.style.alignItems = Align.Center;
            PK.Abs(col, left: P(40), right: P(40), top: p.H * (p.Phone ? .14f : .2f));
            PK.T(col, p, $"Chapter {to - 1}: {GameplayRules.ChapterOf(firstLevel - 1)} complete!", 30, GameTheme.T("#9fe8ff"), true, TextAnchor.MiddleCenter);
            var title = PK.T(col, p, $"Chapter {to}: {GameplayRules.ChapterOf(firstLevel)}", 92, null, true, TextAnchor.MiddleCenter);
            CpPulse(c, title);
            var text = PK.N(col, p, GameplayRules.IslandChapterText(to), 24, GameTheme.T("#dff1ff"), TextAnchor.MiddleCenter, true);
            text.style.maxWidth = P(900);

            // the new places, as tiles
            var row = PK.Row(col, P(14));
            row.style.justifyContent = Justify.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = P(16);
            var chapters = Sandbox?.Chapters;
            if (chapters != null)
                foreach (var name in chapters.TourNames(to))
                {
                    var t = PK.Centre(CpGradBox(row, "tile2", GameTheme.T("#13507c"), GameTheme.T("#0e3a66"), p.U(3), p.U(16), 0));
                    PK.Size(t, P(230), P(96));
                    PK.Pad(t, P(10), P(6), P(10), P(6));
                    PK.N(t, p, name, 23, GameTheme.T("#ffffff"), TextAnchor.MiddleCenter, true);
                }
            PK.N(col, p, "Everything you built stays where it is: the new land rises around it.", 18, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter, true).style.marginTop = P(10);

            var go = PK.Btn(c.Root, p, "green", "See it grow", 300, 84, 32, () =>
            {
                Back();
                var sb = Sandbox;
                if (sb == null) return;
                IslandAudio.Play("level_up", null, 0.9f, 0f, 1f);
                sb.GrowIsland(to, () => Say("Explore your new land!", 3f));
                if (sb.Chapters != null && sb.Chapters.Growing) Open("growtour");
            });
            PK.Abs(go, left: p.W / 2f - P(150), bottom: p.B + P(p.Phone ? 30 : 60));
        }

        /// <summary>See-through while the island grows: the place being shown, and a tap anywhere skips.</summary>
        private void GrowTour(Ctx c)
        {
            var p = c.P;
            var hit = PK.Inset(PK.Box(c.Root));
            hit.pickingMode = PickingMode.Position;
            hit.RegisterCallback<PointerDownEvent>(_ => Sandbox?.Chapters?.Skip());
            // the place's name on a ribbon near the top
            var ribbon = PK.Centre(PK.Box(c.Root));
            PK.Abs(ribbon, left: 0, right: 0, top: p.P(p.Phone ? 70 : 120), height: p.P(110));
            ribbon.pickingMode = PickingMode.Ignore;
            var label = PK.T(ribbon, p, "", 64, null, true, TextAnchor.MiddleCenter);
            label.pickingMode = PickingMode.Ignore;
            CpPulse(c, label);
            var hint = PK.T(c.Root, p, "Tap to skip", 24, GameTheme.T("#ffffff", .9f), true, TextAnchor.MiddleCenter);
            PK.Abs(hint, left: 0, right: 0, bottom: p.B + p.P(40));
            hint.pickingMode = PickingMode.Ignore;
            string shown = "";
            c.Live(() =>
            {
                var now = Sandbox?.Chapters?.TourLabel ?? "";
                if (now == shown) return;
                shown = now;
                label.text = now.Length > 0 ? Loc.Of(now) : "";
                ribbon.style.backgroundColor = now.Length > 0 ? GameTheme.T("#062050", .55f) : new Color(0, 0, 0, 0);
            });
        }
    }
}
