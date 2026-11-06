#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Farmverse.Presentation.UI.Game
{
    // Drag and drop from the panels into the world, wherever the camera is looking:
    //  - a build card dragged up out of the build band becomes the building under the finger, put down
    //    where it's let go (with an Undo for a few seconds);
    //  - the tools on a building's panel (seeds, sickle, feed, a machine's inputs, the hand that collects)
    //    work with a tap, or drag one and sweep it over the buildings: every one it passes that takes it
    //    gets it (sowing a row of fields in one stroke), the ones that don't say why when let go.
    // The pointer is read from the Input System each frame, so a drag goes on after the panel it began
    // in is rebuilt or closed.
    public sealed partial class GameUi
    {
        /// <summary>How far (screen pixels) a press moves before it's a drag.</summary>
        private const float DragFrom = 18f;

        private BuildingCatalogue.Entry? _buildPress;
        private Vector2 _buildPressAt;
        private BuildingCatalogue.Entry? _buildDrag;

        private (int uid, Tool tool)? _toolPress;
        private Vector2 _toolPressAt;
        private Tool? _toolDrag;
        private readonly HashSet<int> _sprayed = new HashSet<int>();
        private int _sprayDone;
        private string? _sprayWhy;
        private VisualElement? _dragIcon;

        private VisualElement? _undo;
        private IVisualElementScheduledItem? _undoHide;

        /// <summary>A building card being dragged into the world (the world shows on its own meanwhile).</summary>
        private bool DraggingBuild => _buildDrag != null;

        // ------------------------------------------------------------------ the pointer

        /// <summary>The pointer in screen pixels (y up) and whether it's held (one finger, or the left button).</summary>
        private static bool PointerNow(out Vector2 at)
        {
            if (Touch.activeTouches.Count > 0)
            {
                at = Touch.activeTouches[0].screenPosition;
                return true;
            }
            var m = Mouse.current;
            at = m != null ? m.position.ReadValue() : default;
            return m != null && m.leftButton.isPressed;
        }

        private Vector2 ScreenToPanel(Vector2 screen)
        {
            var panel = _root?.panel;
            var flipped = new Vector2(screen.x, Screen.height - screen.y);
            return panel != null ? RuntimePanelUtils.ScreenToPanel(panel, flipped) : flipped;
        }

        /// <summary>Called every frame from <see cref="Update"/>.</summary>
        private void DragTick()
        {
            var held = PointerNow(out var at);

            if (_buildDrag != null)
            {
                if (!held) EndBuildDrag(at);
                return;
            }
            if (_buildPress is { } e)
            {
                var d = at - _buildPressAt;
                if (!held || Mathf.Abs(d.x) > DragFrom) _buildPress = null;   // a tap, or the band scrolling sideways
                else if (d.y > DragFrom && d.y > Mathf.Abs(d.x))
                {
                    _buildPress = null;
                    StartBuildDrag(e, at);
                    return;
                }
            }

            if (_toolDrag is { } t)
            {
                if (!held) EndToolDrag();
                else ToolDragMove(t, at);
                return;
            }
            if (_toolPress is { } tp)
            {
                if (!held) _toolPress = null;
                else if ((at - _toolPressAt).magnitude > DragFrom)
                {
                    _toolPress = null;
                    StartToolDrag(tp.uid, tp.tool, at);
                }
            }
        }

        // ------------------------------------------------------------------ building cards

        /// <summary>Lets a build card be dragged up into the world (a tap still buys it at the middle of the view).</summary>
        private void PlayDragBuild(VisualElement card, BuildingCatalogue.Entry e)
        {
            card.RegisterCallback<PointerDownEvent>(_ =>
            {
                PointerNow(out var at);
                _buildPress = e;
                _buildPressAt = at;
            }, TrickleDown.TrickleDown);
        }

        private void StartBuildDrag(BuildingCatalogue.Entry e, Vector2 at)
        {
            var s = Sandbox;
            if (s == null) return;
            var need = GameplayRules.UnlockLevel(e.id);
            if (s.IslandLevel < need)
            {
                Say($"Opens at island level {need}", 2f);
                IslandAudio.Play("error", null, 0.5f);
                return;
            }
            if (s.CantBuy(e) is { } why)
            {
                Say(why, 2.5f);
                IslandAudio.Play("error", null, 0.5f);
                return;
            }
            if (s.CoinsNow < e.price)
            {
                Open("notenough", new PlayNeed("coins", e.price - s.CoinsNow, "build the " + e.displayName.ToLowerInvariant()));
                return;
            }
            if (!s.BeginDragBuy(e, at)) return;
            _buildDrag = e;
            s.HoldCamera(true);
            _playBuildMenu = false;   // the band gets out of the way while the building is carried
            IslandAudio.Play("pop");
            MarkDirty();
        }

        private void EndBuildDrag(Vector2 at)
        {
            var e = _buildDrag!;
            _buildDrag = null;
            var s = Sandbox;
            if (s == null) return;
            s.HoldCamera(false);
            var placed = s.EndDragBuy(Over(at));
            if (placed == null)
            {
                Say("It doesn't fit there", 1.6f);
                IslandAudio.Play("error", null, 0.5f);
                _playBuildMenu = true;   // back to the band to try again
                MarkDirty();
                return;
            }
            ShowUndo(e.displayName + " placed", () =>
            {
                if (s.UndoBuy(placed, e.price)) Say(e.displayName + " taken back", 1.6f);
                else Say("Too late to undo: the builders have started", 2.2f);
                MarkDirty();
            });
            MarkDirty();
        }

        /// <summary>A pill under the toast with an Undo button, for a few seconds.</summary>
        private void ShowUndo(string text, Action undo)
        {
            if (_toastLayer == null) return;
            HideUndo();
            var p = new Pt(_size.x, _size.y, .55f);
            var pill = PK.Row(null, p.P(14));
            pill.style.position = Position.Absolute;
            pill.style.bottom = p.B + p.P(150);
            pill.style.alignSelf = Align.Center;
            pill.style.alignItems = Align.Center;
            PK.Pad(pill, p.P(22), p.P(10), p.P(12), p.P(10));
            pill.style.backgroundColor = GameTheme.T("#061e46", .95f);
            Kit.Round(pill, p.U(40));
            Kit.Border(pill, p.U(3), GameTheme.T("#0b1d3a"));
            pill.pickingMode = PickingMode.Position;
            PK.T(pill, p, text, 22);
            PK.Btn(pill, p, "yellow", "Undo", 0, 50, 20, () =>
            {
                HideUndo();
                undo();
            });
            _toastLayer.Add(pill);
            _undo = pill;
            _undoHide = pill.schedule.Execute(HideUndo).StartingIn(5000);
        }

        private void HideUndo()
        {
            _undoHide?.Pause();
            _undoHide = null;
            _undo?.RemoveFromHierarchy();
            _undo = null;
        }

        // ------------------------------------------------------------------ tools

        /// <summary>What the tools on a building's card look like now (the card is rebuilt when it changes).</summary>
        private string PlayToolsKey(int uid)
        {
            var host = Host;
            return host == null ? "" : string.Join(",", host.ToolsFor(uid).Select(t => t.Kind + ":" + t.Item));
        }

        /// <summary>Buildings whose card is the tool panel: fields, animal houses, machines.</summary>
        private bool PlayTakesTools(int uid)
        {
            var eco = Economy;
            return eco != null && eco.IsBuilt(uid) && (eco.Plots.ContainsKey(uid) || eco.Animals.ContainsKey(uid) || eco.Machines.ContainsKey(uid));
        }

        /// <summary>
        /// A building that takes tools, as a panel along the bottom (the view above stays clear): its name and
        /// state, its buttons, and the tools it takes now, big enough to grab. Tap one to use it here, or drag
        /// it over the fields and buildings.
        /// </summary>
        private void PlayToolBand(Ctx c, int uid, bool tapped)
        {
            var p = c.P;
            var s = Sandbox!;
            var eco = Economy!;
            var host = Host!;
            var building = host.Building(uid) ?? s.Selected;
            if (building == null) return;
            c.Watch(() => PlayToolsKey(uid));

            var band = PK.Box(c.Root);
            band.pickingMode = PickingMode.Position;
            PK.Abs(band, left: 0, right: 0, bottom: 0);
            band.style.backgroundColor = GameTheme.T("#0b2a52");
            Kit.Nine(band, GameArt.Get("Popup_FullWidth03_Single_Navy"), 4, 20, 4, 20, p.U(14) / 20f);
            PK.Pad(band, p.L + p.P(20), p.P(14), p.R + p.P(20), p.B + p.P(12));

            var head = PK.Row(band, p.P(10));
            head.style.minHeight = p.P(58);
            head.style.flexShrink = 0;
            head.style.alignItems = Align.Center;
            var info = PK.Col(head);
            info.style.flexShrink = 1;
            var title = PK.Row(info, p.P(8));
            PK.T(title, p, IslandEconomyHost.NameOf(building.id), 24);
            var lvl = PlayLevelOf(uid);
            if (lvl > 0) PlayTag(title, p, "Lv " + lvl, 14, GameTheme.C("#ffd23a"), PK.Ink).style.alignSelf = Align.Center;
            var st = PK.N(info, p, "", 16, GameTheme.T("#bfe6ff"));
            c.Live(() => st.text = Loc.Of(PlayStatus(uid)));
            PK.Grow(head);

            if (eco.CanLevel(uid) || eco.UpgradeCost(uid) != null) PK.Btn(head, p, "yellow", "Upgrade", 0, 50, 18, () => Open("upgrade", uid));
            if (eco.WorkJobs(uid).Count > 0) PK.Btn(head, p, "blue", "Workers", 0, 50, 18, () => Open("workjobs", uid));
            PK.Btn(head, p, "gray", "More", 0, 50, 18, () =>
            {
                _playFullUid = uid;
                MarkDirty();
            });
            if (tapped) PlayClose(head, p, 56, 60, () => { s.Deselect(); MarkDirty(); });

            var tools = host.ToolsFor(uid);
            if (tools.Count == 0)
            {
                var none = PK.N(band, p, "Nothing to do here right now.", 18, GameTheme.T("#bfe6ff"));
                none.style.marginTop = p.P(10);
                none.style.marginBottom = p.P(6);
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.marginTop = p.P(10);
            scroll.style.height = p.P(124);
            scroll.style.flexShrink = 0;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            band.Add(scroll);
            var first = true;
            foreach (var tool in tools)
            {
                var chip = PlayToolChip(scroll.contentContainer, p, uid, tool);
                if (!first) chip.style.marginLeft = p.P(12);
                first = false;
            }
            var hint = PK.N(band, p, "Tap to use here, or drag onto fields and buildings", 15, GameTheme.T("#bfe6ff"));
            hint.style.marginTop = p.P(6);
        }

        private VisualElement PlayToolChip(VisualElement parent, Pt p, int uid, Tool tool)
        {
            var host = Host!;
            var chip = PK.Col(parent);
            chip.style.alignItems = Align.Center;
            chip.style.justifyContent = Justify.Center;
            PK.Size(chip, p.P(110), p.P(120));
            Kit.Nine(chip, GameArt.Get("CardFrame06_Bg_Blue"), 28, 40, 28, 20, p.U(14) / 40f);
            var item = host.ToolItem(uid, tool);
            var icon = tool.Kind == ToolKind.Sickle && item.Length == 0
                ? PK.Img(null, "Icon_Shovel", p.P(60))
                : ShopGoodIcon(p, item, p.P(60));
            icon.pickingMode = PickingMode.Ignore;
            foreach (var child in icon.Query<VisualElement>().ToList()) child.pickingMode = PickingMode.Ignore;
            chip.Add(icon);
            var cap = PK.T(chip, p, ToolLabel(tool), 16, align: TextAnchor.MiddleCenter);
            cap.pickingMode = PickingMode.Ignore;
            cap.style.marginTop = p.P(4);
            chip.RegisterCallback<PointerDownEvent>(_ =>
            {
                PointerNow(out var at);
                _toolPress = (uid, tool);
                _toolPressAt = at;
            }, TrickleDown.TrickleDown);
            PK.Tap(chip, p, () =>
            {
                if (_toolDrag != null) return;
                UseOne(uid, tool, true);
            }, "");
            return chip;
        }

        private static string ToolLabel(Tool t) => t.Kind switch
        {
            ToolKind.Seed => IslandEconomyHost.PrettyOf(t.Item),
            ToolKind.Sickle => "Harvest",
            ToolKind.Hand => "Collect",
            ToolKind.Feed => "Feed",
            _ => "Fill",
        };

        /// <summary>Uses a tool on one building; says why not when it doesn't take (if asked to).</summary>
        private bool UseOne(int uid, Tool tool, bool say)
        {
            var host = Host;
            if (host == null) return false;
            var use = host.UseTool(uid, tool);
            if (use.Ok) return true;
            var why = IslandEconomyHost.RefusalText(use.Refusal, tool);
            if (say && why.Length > 0)
            {
                Say(why, 1.8f);
                IslandAudio.Play("error", null, 0.5f);
            }
            else _sprayWhy = why;
            return false;
        }

        private void StartToolDrag(int uid, Tool tool, Vector2 at)
        {
            var s = Sandbox;
            var host = Host;
            if (s == null || host == null || _toastLayer == null) return;
            _toolDrag = tool;
            _sprayed.Clear();
            _sprayDone = 0;
            _sprayWhy = null;
            s.HoldCamera(true);
            var p = new Pt(_size.x, _size.y, .55f);
            var item = host.ToolItem(uid, tool);
            var icon = tool.Kind == ToolKind.Sickle && item.Length == 0 ? PK.Img(null, "Icon_Shovel", p.P(72)) : ShopGoodIcon(p, item, p.P(72));
            icon.style.position = Position.Absolute;
            icon.pickingMode = PickingMode.Ignore;
            foreach (var child in icon.Query<VisualElement>().ToList()) child.pickingMode = PickingMode.Ignore;
            _toastLayer.Add(icon);
            _dragIcon = icon;
            ToolDragMove(tool, at);
        }

        private void ToolDragMove(Tool tool, Vector2 at)
        {
            var s = Sandbox;
            if (s == null) return;
            if (_dragIcon != null)
            {
                var pos = ScreenToPanel(at);
                var size = _dragIcon.resolvedStyle.width;
                if (float.IsNaN(size) || size < 1f) size = 60f;
                // just above the finger, so the finger doesn't hide it
                _dragIcon.style.left = pos.x - size / 2f;
                _dragIcon.style.top = pos.y - size * 1.4f;
            }
            if (Over(at)) return;
            var b = s.BuildingAt(at);
            var ok = b != null && Host != null && Host.CanUse(b.uid, tool) == ToolRefusal.Ok;
            if (_dragIcon != null) _dragIcon.style.scale = new Scale(Vector3.one * (ok ? 1.2f : 1f));
            if (b == null || !_sprayed.Add(b.uid)) return;
            if (UseOne(b.uid, tool, false)) _sprayDone++;
        }

        private void EndToolDrag()
        {
            var tool = _toolDrag!.Value;
            _toolDrag = null;
            Sandbox?.HoldCamera(false);
            _dragIcon?.RemoveFromHierarchy();
            _dragIcon = null;
            if (_sprayDone > 1)
                Say(tool.Kind switch
                {
                    ToolKind.Seed => $"Sowed {_sprayDone} fields",
                    ToolKind.Sickle => $"Harvested {_sprayDone} fields",
                    ToolKind.Feed => $"Fed {_sprayDone} houses",
                    ToolKind.Hand => $"Collected from {_sprayDone}",
                    _ => $"Filled {_sprayDone}",
                }, 1.6f);
            else if (_sprayDone == 0 && !string.IsNullOrEmpty(_sprayWhy))
            {
                Say(_sprayWhy!, 1.8f);
                IslandAudio.Play("error", null, 0.5f);
            }
            _sprayed.Clear();
            MarkDirty();
        }
    }
}
