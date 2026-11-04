#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Presentation.World;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Customize (Home ▸ Customize): dress each kind of person on the island — Me, the army, labourers,
    /// builders and each of the townsfolk — from the Creative Characters wardrobe. A live model on the left
    /// (drag to turn it), the wardrobe on the right: one strip of slots (Hat, Hair, Face…), a grid of what
    /// fits in the chosen slot. A tap wears it and keeps it: everyone of that role changes at once.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegCustomize = Register("customize", UiLayer.Page, (ui, c) => ui.CustomizePage(c), .55f);

        private static string _czTab = "player";
        private static string _czRole = "player";
        private static string _czSlot = "Hat";

        /// <summary>What each slot is called on screen, in wardrobe order.</summary>
        private static readonly (string slot, string label, string icon)[] CzSlots =
        {
            ("Hat", "Hat", "Icon_MenuIcon03_Ornament_n"), ("Hairstyle", "Hair", "Icon_PictoIcon_User"), ("Faces", "Face", "Icon_PictoIcon_Mask"),
            ("Glasses", "Glasses", "Icon_PictoIcon_Mask"), ("Mustache", "Beard", "Icon_PictoIcon_User"), ("Accessories", "Extras", "Icon_MenuIcon03_Weapon_n"),
            ("Outerwear", "Top", "Icon_PictoIcon_Cloth"), ("Pants", "Pants", "Icon_PictoIcon_Cloth"), ("Shoes", "Shoes", "Icon_MenuIcon03_Shoes_n"),
            ("Gloves", "Gloves", "Icon_MenuIcon03_Shield_n"), ("Body", "Skin", "Icon_PictoIcon_User"),
        };

        private void CustomizePage(Ctx c)
        {
            var p = c.P;
            var cat = CharacterCatalog.Load();
            var (body, _, _) = ShopFrame(c, "Customize", null);
            if (cat == null || cat.parts == null || cat.people.Count == 0)
            {
                var none = PK.Centre(PK.Panel(body, p));
                none.style.flexGrow = 1;
                HomeMsg(none, p, "The wardrobe isn't built yet.<br>Run <b>Farmverse ▸ Art ▸ Build ITHappy Characters</b>.", 24);
                return;
            }

            // who: the tab's people (townsfolk have several)
            if (!CharacterCatalog.Tabs.Any(t => t.id == _czTab)) _czTab = "player";
            var people = cat.In(_czTab).ToList();
            if (people.Count == 0) { _czTab = "player"; people = cat.In(_czTab).ToList(); }
            if (people.Count == 0) people = cat.people.ToList();
            var who = people.FirstOrDefault(x => x.role == _czRole) ?? people[0];
            _czRole = who.role;
            var slots = cat.parts.slots.Where(s => s.options.Count > 1 || (s.canBeEmpty && s.options.Count > 0)).ToList();
            var slotRows = CzSlots.Where(s => slots.Any(x => x.name == s.slot)).ToList();
            // whole outfits from the packs come first (bought with gems)
            if (cat.costumes.Count > 0) slotRows.Insert(0, (CharacterLook.CostumeSlot, "Costume", "Icon_Crown"));
            if (slotRows.All(s => s.slot != _czSlot)) _czSlot = slotRows.Count > 0 ? slotRows[0].slot : "Hat";

            var preview = CharacterPreview.Get();
            var look = CharacterLook.LookOf(who.role);
            if (string.IsNullOrEmpty(look)) look = who.defaultLook;
            if (who.prefab != null) preview.Show(who.prefab, look, cat.animator);
            c.Live(preview.Touch);


            // ---------------------------------------------------------------- the model
            var left = PK.Panel(body, p);
            left.style.width = new Length(p.Phone ? 34 : 36, LengthUnit.Percent);
            left.style.flexShrink = 0;
            left.style.flexDirection = FlexDirection.Column;
            left.style.marginRight = p.P(16);
            PK.Pad(left, p.P(12), p.P(12), p.P(12), p.P(12));
            var stage = PK.Box(left);
            stage.style.flexGrow = 1;
            stage.pickingMode = PickingMode.Position;
            Kit.Round(stage, p.U(14));
            PK.Grad(stage, "czstage", true, (0f, GameTheme.T("#9fdcff")), (1f, GameTheme.T("#3e8fd6")));
            var pic = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            PK.Inset(pic);
            pic.style.position = Position.Absolute;
            stage.Add(pic);
            c.Live(() => { if (preview.Texture != null && pic.image != preview.Texture) pic.image = preview.Texture; });
            var dragging = false; float lastX = 0;
            stage.RegisterCallback<PointerDownEvent>(e => { dragging = true; lastX = e.position.x; stage.CapturePointer(e.pointerId); });
            stage.RegisterCallback<PointerMoveEvent>(e => { if (!dragging) return; preview.Spin((e.position.x - lastX) * 0.6f); lastX = e.position.x; });
            stage.RegisterCallback<PointerUpEvent>(e => { dragging = false; stage.ReleasePointer(e.pointerId); });
            stage.RegisterCallback<PointerCancelEvent>(_ => dragging = false);
            var hint = PK.N(stage, p, "Drag to turn", 15, GameTheme.T("#eaf7ff", .85f), TextAnchor.MiddleCenter);
            PK.Abs(hint, left: 0, right: 0, bottom: p.P(8));

            var name = PK.T(left, p, who.label, 26, null, true, TextAnchor.MiddleCenter);
            name.style.marginTop = p.P(10);
            var acts = PK.Row(left, p.P(12));
            acts.style.justifyContent = Justify.Center;
            acts.style.marginTop = p.P(8);
            PK.Btn(acts, p, "blue", "Surprise me", 170, 56, 19, () => CzWear(who, CzRandom(cat, who), preview));
            PK.Btn(acts, p, "gray", "Reset", 120, 56, 19, () => CzWear(who, who.defaultLook, preview));

            // ---------------------------------------------------------------- the wardrobe
            var right = PK.Panel(body, p);
            right.style.flexGrow = 1;
            right.style.minWidth = 0;
            right.style.flexDirection = FlexDirection.Column;
            PK.Pad(right, p.P(14), p.P(12), p.P(14), p.P(12));

            PK.Seg(right, p, CharacterCatalog.Tabs.Select(t => (t.id, t.label)).ToArray(), _czTab, id => { _czTab = id; _czRole = ""; MarkDirty(); }, p.Phone ? 18 : 22, p.Phone ? 48 : 56);

            if (people.Count > 1)
            {
                var row = PK.Row(right, p.P(10));
                row.style.flexShrink = 0;
                row.style.marginTop = p.P(10);
                foreach (var person in people)
                {
                    var on = person.role == who.role;
                    var b = PK.Btn(row, p, on ? "green" : "gray", person.label, 150, 50, 18, () => { _czRole = person.role; MarkDirty(); });
                    b.style.flexShrink = 1;
                }
            }

            // slots: a wrapping row of chips with their icons
            var chips = PK.Row(right, p.P(8));
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.flexShrink = 0;
            chips.style.marginTop = p.P(10);
            foreach (var (slot, label, icon) in slotRows)
            {
                var on = slot == _czSlot;
                var chip = PK.Row(chips, p.P(6));
                chip.style.height = p.P(44);
                chip.style.marginBottom = p.P(8);
                PK.Pad(chip, p.P(12), 0, p.P(14), 0);
                chip.style.backgroundColor = on ? GameTheme.T("#27a9ff") : GameTheme.T("#0d3f7a");
                Kit.Round(chip, p.P(22));
                if (on) Kit.Border(chip, p.U(2), Color.white);
                PK.Img(chip, icon, p.P(22)).style.opacity = on ? 1f : .8f;
                PK.T(chip, p, label, 17, on ? Color.white : GameTheme.T("#9fd0ff"));
                var sid = slot;
                PK.Tap(chip, p, () => { _czSlot = sid; MarkDirty(); });
            }

            if (_czSlot == CharacterLook.CostumeSlot)
            {
                CzCostumes(right, p, cat, who, preview);
                return;
            }

            // options of the chosen slot: a grid of worn-part pictures, gaps between the tiles
            var slotDef = cat.parts.Find(_czSlot);
            var wearing = CharacterLook.Parse(preview.Look);
            wearing.TryGetValue(_czSlot, out var now);
            var sv = ShopScroller(right, false, p.P(10));
            var grid = PK.Grid(sv.contentContainer, p.P(14), p.P(14));
            grid.style.paddingTop = p.P(4);
            var tile = p.Phone ? 96f : 112f;
            if (slotDef != null)
            {
                if (slotDef.canBeEmpty) CzTile(grid, p, tile, null, "None", string.IsNullOrEmpty(now), () => CzPick(who, _czSlot, "", preview));
                foreach (var mesh in slotDef.options)
                {
                    if (mesh == null) continue;
                    var m = mesh.name;
                    CzTile(grid, p, tile, preview.Thumb(cat.parts, _czSlot, m), null, now == m, () => CzPick(who, _czSlot, m, preview));
                }
            }
        }

        /// <summary>
        /// The costumes: whole outfits from the packs (Santa from the Christmas pack, every Zombies Mini outfit on a
        /// human body). Owned ones are worn with a tap; the others show their gem price and are bought first.
        /// While a costume is on, the other slots change the clothes underneath (seen again with "None").
        /// </summary>
        private void CzCostumes(VisualElement right, Pt p, CharacterCatalog cat, CharacterCatalog.Person who, CharacterPreview preview)
        {
            var wearing = CharacterLook.Parse(preview.Look);
            wearing.TryGetValue(CharacterLook.CostumeSlot, out var now);
            var eco = Economy;
            var sv = ShopScroller(right, false, p.P(10));
            var grid = PK.Grid(sv.contentContainer, p.P(14), p.P(14));
            grid.style.paddingTop = p.P(4);
            var tile = p.Phone ? 110f : 128f;
            CzTile(grid, p, tile, null, "Own clothes", string.IsNullOrEmpty(now), () => CzPick(who, CharacterLook.CostumeSlot, "", preview));
            foreach (var costume in cat.costumes)
            {
                var co = costume;
                var owned = eco != null && eco.HasLook(co.id);
                var price = Wallet.Price("look_" + co.id);
                var pic = Resources.Load<Texture2D>("GameUi/Art/Isl_p_costume_" + co.id);
                var cell = CzTileBox(grid, p, tile, pic, now == co.id, () =>
                {
                    if (owned) { CzPick(who, CharacterLook.CostumeSlot, co.id, preview); return; }
                    if (price <= 0) { Say("This costume isn't on sale yet"); return; }
                    Open("confirm", new HomeConfirmArgs
                    {
                        Title = co.label + " costume",
                        Text = $"Buy the {co.label} costume ({co.pack} pack) for <b>{price}</b> gems? It's yours for good, for anyone on your island to wear.",
                        Ok = "Buy",
                        OnOk = () => ShopBuy("look_" + co.id, () =>
                        {
                            Sandbox?.GrantLook(co.id);
                            CzPick(who, CharacterLook.CostumeSlot, co.id, preview);
                            Say(co.label + " costume is yours!");
                        }),
                    });
                });
                var label = PK.T(cell, p, co.label, 15, Color.white, false, TextAnchor.MiddleCenter);
                PK.Abs(label, left: 0, right: 0, bottom: p.P(tile * .06f));
                if (!owned && price > 0)
                {
                    var tag = PK.Row(cell, p.P(4));
                    PK.Abs(tag, right: p.P(6), top: p.P(6));
                    PK.Pad(tag, p.P(8), p.P(2), p.P(8), p.P(2));
                    tag.style.backgroundColor = GameTheme.T("#041634", .85f);
                    Kit.Round(tag, p.P(12));
                    PK.Img(tag, "ResourceBar_Icon_Gem", p.P(20));
                    PK.T(tag, p, price.ToString(), 15, Color.white);
                }
            }
        }

        /// <summary>A wardrobe tile with a picture, returned so a costume can carry its name and price.</summary>
        private static VisualElement CzTileBox(VisualElement grid, Pt p, float s, Texture2D? thumb, bool sel, Action pick)
        {
            var e = PK.Centre(PK.Box(grid));
            PK.Size(e, p.P(s), p.P(s * 1.06f));
            Kit.Nine(e, GameArt.Get("ItemFrame01_Single_" + (sel ? "Yellow" : "Blue")), 40, 40, 40, 46, p.P(s * .2f) / 40f);
            if (thumb != null)
            {
                var ic = PK.Box(e);
                PK.Size(ic, p.P(s * .78f), p.P(s * .78f));
                ic.style.marginBottom = p.P(s * .12f);
                Kit.Contain(ic, thumb);
            }
            if (sel)
            {
                var f = PK.Inset(PK.Box(e), -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f);
                Kit.Picture(f, GameArt.Get("ItemFrame01_f"));
            }
            PK.Tap(e, p, pick);
            return e;
        }

        private static void CzTile(VisualElement grid, Pt p, float s, Texture2D? thumb, string? word, bool sel, Action pick)
        {
            var e = PK.Centre(PK.Box(grid));
            PK.Size(e, p.P(s), p.P(s * 1.06f));
            Kit.Nine(e, GameArt.Get("ItemFrame01_Single_" + (sel ? "Yellow" : "Blue")), 40, 40, 40, 46, p.P(s * .2f) / 40f);
            if (thumb != null)
            {
                var ic = PK.Box(e);
                PK.Size(ic, p.P(s * .8f), p.P(s * .8f));
                Kit.Contain(ic, thumb);
            }
            else if (word != null)
            {
                PK.Img(e, "Icon_PictoIcon_Close", p.P(s * .3f)).style.opacity = .7f;
                PK.T(e, p, word, s * .16f, GameTheme.T("#dff1ff"));
            }
            if (sel)
            {
                var f = PK.Inset(PK.Box(e), -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f);
                Kit.Picture(f, GameArt.Get("ItemFrame01_f"));
            }
            PK.Tap(e, p, pick);
        }

        /// <summary>Wear one part (or nothing) in a slot, and keep it.</summary>
        private void CzPick(CharacterCatalog.Person who, string slot, string mesh, CharacterPreview preview)
        {
            var d = CharacterLook.Parse(preview.Look);
            d[slot] = mesh;
            CzWear(who, CharacterLook.Write(d), preview);
        }

        private void CzWear(CharacterCatalog.Person who, string look, CharacterPreview preview)
        {
            preview.Dress(look);
            CharacterLook.Save(who.role, look);
            IslandAudio.Play("pop", null, 0.5f, 0f);
            MarkDirty();
        }

        /// <summary>A random outfit: every slot gets one of its parts (or nothing, where that's allowed).</summary>
        private static string CzRandom(CharacterCatalog cat, CharacterCatalog.Person who)
        {
            var d = CharacterLook.Parse(who.defaultLook);
            foreach (var s in cat.parts!.slots)
            {
                var opts = s.options.Where(m => m != null).Select(m => m.name).ToList();
                if (opts.Count == 0) continue;
                if (s.name == "Body") continue;
                var none = s.canBeEmpty && (s.name is "Hat" or "Glasses" or "Mustache" or "Accessories" or "Gloves") && UnityEngine.Random.value < 0.35f;
                d[s.name] = none ? "" : opts[UnityEngine.Random.Range(0, opts.Count)];
            }
            return CharacterLook.Write(d);
        }
    }
}
