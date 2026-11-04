#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Loading (the pack's Loading screen), to loading.html: full-bleed island art, the Farmverse logo,
    /// a tip, the yellow bar with "Loading…%", the spinner while connecting, then TAP TO START → Home.
    /// Progress follows the island really getting ready (scene found → economy up → running). On the very
    /// first launch the Terms popup shows over it before the player can start.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegHome_Loading = Register("loading", UiLayer.Base, (ui, c) => ui.HomeLoading(c), .55f, covers: true);

        private static readonly (string art, string text)[] HomeTips =
        {
            ("G_Water", "A <b>Well</b> keeps fields within 30 m safe from drought."),
            ("G_Autumn", "Autumn crops give <b>+2</b> yield — plant before it ends!"),
            ("Icon_Shield", "A <b>Raid Shield</b> protects your island for 8 hours."),
            ("G_Flower", "Flower beds near hives make honey up to <b>2×</b> faster."),
            ("ResourceBar_Icon_Gold", "Selling too much of one crop lowers the price <b>shops</b> pay."),
        };

        private float _homeShown = -1f, _homeProgress;

        private void HomeLoading(Ctx c)
        {
            var p = c.P;
            var root = c.Root;
            if (Sandbox != null) Sandbox.Lobby = true;
            if (_homeShown < 0) _homeShown = Time.unscaledTime;
            float U(float n) => p.U(n);

            // the design's numbers for each device (literal points)
            var phone = p.Phone;
            float logoTop = phone ? 26 : 90, logoFs = phone ? 70 : 150, spinS = phone ? 44 : 72, pad = phone ? 10 : 40,
                barH = phone ? 32 : 56, tipFs = phone ? 13 : 20, tipGap = phone ? 8 : 16, tapFs = phone ? 30 : 52;

            var bg = PK.Inset(PK.Box(root));
            bg.pickingMode = PickingMode.Position;
            Kit.Cover(bg, GameArt.Get("Home_LoadingBg"));

            Label Small(string text) => PK.N(root, p, text, 1, GameTheme.Light(.75f));
            var legal = Small("© Farmverse");
            legal.style.fontSize = U(11);
            PK.Abs(legal, left: p.L + U(10), top: U(8));
            var ver = Small("v" + UnityEngine.Application.version);
            ver.style.fontSize = U(11);
            PK.Abs(ver, right: p.R + U(10), top: U(8));

            // the logo (rendered from the mockup's type logo: Home_Logo, 300 px type with 60/70 px margins)
            var s = U(logoFs) / 300f;
            const float logoScale = .7f;   // smaller than the mockup's box, still centred on it
            var logo = PK.Box(root);
            PK.Size(logo, 1754 * s * logoScale, 516 * s * logoScale);
            PK.Abs(logo, left: (p.W - 1754 * s * logoScale) / 2f, top: U(logoTop) - 60 * s + 516 * s * (1 - logoScale) / 2f);
            Kit.Contain(logo, GameArt.Get("Home_Logo"));   // the painted logo, centred in the design's logo box

            var bottom = PK.Col(root, U(tipGap));
            PK.Abs(bottom, left: p.L + U(pad), right: p.R + U(pad), bottom: p.B + U(pad));
            bottom.style.alignItems = Align.Center;

            var spin = HomeSpinner(bottom, p, U(spinS), c);
            spin.style.display = DisplayStyle.None;

            var tip = PK.Row(bottom, U(10));
            tip.style.backgroundColor = GameTheme.T("#061e46", .55f);
            Kit.Round(tip, U(tipFs * 2));
            PK.Pad(tip, U(8), U(6), U(18), U(6));
            tip.style.maxWidth = new Length(100, LengthUnit.Percent);
            PK.Clip(tip);
            var tipImg = PK.Img(tip, HomeTips[0].art, U(tipFs * 1.7f));
            var tipText = PK.N(tip, p, "", 1);
            tipText.style.fontSize = U(tipFs);
            tipText.enableRichText = true;
            tipText.style.textOverflow = TextOverflow.Ellipsis;
            tipText.style.flexShrink = 1;
            tipText.style.overflow = Overflow.Hidden;
            tipText.style.textShadow = new TextShadow { offset = new Vector2(0, U(2)), blurRadius = 0, color = GameTheme.Shade(.35f) };

            var tap = PK.T(bottom, p, "TAP TO START", 1);
            tap.style.fontSize = U(tapFs);
            tap.style.unityTextOutlineWidth = U(6);
            tap.style.textShadow = new TextShadow { offset = new Vector2(0, U(6) * .7f), blurRadius = 0, color = PK.Ink };
            tap.style.display = DisplayStyle.None;
            HomePulse(c, tap, 1.06f, 1.1f);

            var bar = PK.Box(bottom);
            bar.style.width = new Length(100, LengthUnit.Percent);
            bar.style.height = U(barH);
            bar.style.backgroundColor = GameTheme.T("#0d1d4f");
            Kit.Border(bar, U(3), PK.Ink);
            Kit.Round(bar, U(barH * .3f));
            var fill = PK.Abs(PK.Box(bar), left: 0, top: 0, bottom: 0);
            fill.style.minWidth = U(barH * .7f);
            HomeNine(fill, p, "Slider_Basic02_Fill_Yellow", 18, 16, 18, 16, barH * .35f);
            var lbl = PK.T(PK.Centre(PK.Inset(PK.Box(bar))), p, "Loading...", 1);
            lbl.style.fontSize = U(barH * .52f);
            lbl.style.unityTextOutlineWidth = U(barH * .08f);
            lbl.style.textShadow = new TextShadow { offset = new Vector2(0, U(barH * .08f) * .7f), blurRadius = 0, color = PK.Ink };
            var shadow = PK.Abs(PK.Box(bar), left: -U(3), right: -U(3), bottom: -U(7), height: U(4));   // 0 4px 0 rgba(0,0,0,.25)
            shadow.style.backgroundColor = GameTheme.Shade(.25f);
            shadow.SendToBack();

            var tipShown = -1;
            var ready = false;
            c.Live(() =>
            {
                // what's really done: the scene, the economy, the island running
                var target = Sandbox == null ? .15f : Host == null ? .45f : !Sandbox.Running ? .8f : 1f;
                var elapsed = Time.unscaledTime - _homeShown;
                _homeProgress = Mathf.MoveTowards(_homeProgress, Mathf.Min(target, elapsed / 1.5f), Time.unscaledDeltaTime * .9f);
                var connecting = target < 1f && elapsed > 6f;
                var done = _homeProgress >= .999f;

                spin.style.display = connecting && !done ? DisplayStyle.Flex : DisplayStyle.None;
                tip.style.display = !connecting && !done ? DisplayStyle.Flex : DisplayStyle.None;
                bar.style.display = done ? DisplayStyle.None : DisplayStyle.Flex;
                tap.style.display = done ? DisplayStyle.Flex : DisplayStyle.None;
                PK.SetPct(fill, connecting ? 12f : _homeProgress * 100f);
                lbl.text = connecting ? Loc.Of("Connecting...") : Loc.Of("Loading...") + Mathf.FloorToInt(_homeProgress * 100f) + "%";

                var ti = (int)(elapsed / 4f) % HomeTips.Length;
                if (ti != tipShown)
                {
                    tipShown = ti;
                    Kit.Picture(tipImg, GameArt.Get(HomeTips[ti].art));
                    tipText.text = Loc.Of(HomeRich(HomeTips[ti].text));
                }

                if (done && !ready)
                {
                    ready = true;
                    if (PlayerPrefs.GetInt("fv_terms_ok", 0) == 0 && TopPopup != "terms") Open("terms");
                }
            });

            PK.Tap(bg, p, () =>
            {
                if (!ready || TopPopup != null) return;
                _homeShown = -1f;
                _homeProgress = 0f;
                Go("home");
            }, "whoosh");
        }
    }
}
