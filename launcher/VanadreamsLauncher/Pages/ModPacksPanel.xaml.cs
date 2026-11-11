using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class PackRow
    {
        public ModPack Pack { get; set; }
        public int Index { get; set; }
        public string Number => (Index + 1).ToString();
        public string Name => Pack.Name;
        public string Detail => Pack.Detail;
        public bool On => Pack.On;
        public bool CanSwitch => Pack.CanSwitch;
        public bool CanUp { get; set; }
        public bool CanDown { get; set; }
        public Visibility ArrowsVisibility => Pack.CanSwitch ? Visibility.Visible : Visibility.Hidden;
        public string SwitchTip => Pack.CanSwitch ? UiText.T("Load this pack, or leave it out") : UiText.T("Unpacked into the game's own files, so it is always on");
    }

    /// <summary>The packs this PC has, with their switches and order. Only XIPivot overlays move or switch (ModPacks).</summary>
    public partial class ModPacksPanel : UserControl
    {
        private List<ModPack> _packs = new List<ModPack>();

        public ModPacksPanel()
        {
            InitializeComponent();
            Loaded += (s, e) => Reload();
        }

        public void Reload()
        {
            var state = App.State;
            var version = state.Version.ExpectedIsPublished ? state.Version.Expected : state.Status.ClientVer;
            _packs = ModPacks.List(state.PacksRoot(version), version, state.HasAshita ? state.AshitaRoot : null);
            Draw();
            var overlays = _packs.Any(p => p.CanSwitch);
            Note.Text = overlays && !ModPacks.PivotInstalled(state.AshitaRoot)
                ? UiText.T("Overlays need XIPivot: tick it on the Addons page and press Install.")
                : "";
        }

        private void Draw()
        {
            var movable = _packs.Select((p, i) => new { p, i }).Where(x => x.p.CanSwitch && x.p.On).Select(x => x.i).ToList();
            var rows = _packs.Select((p, i) => new PackRow
            {
                Pack = p, Index = i,
                CanUp = movable.Contains(i) && movable.IndexOf(i) > 0,
                CanDown = movable.Contains(i) && movable.IndexOf(i) < movable.Count - 1,
            }).ToList();
            List.ItemsSource = rows;
            None.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save()
        {
            try { ModPacks.Save(App.State.AshitaRoot, _packs); }
            catch (Exception ex) { Note.Text = ex.Message; Log.Warn("mod packs: " + ex.Message); }
        }

        private void Switch_Click(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as PackRow;
            if (row == null || !row.Pack.CanSwitch) return;
            row.Pack.On = !row.Pack.On;
            row.Pack.Detail = row.Pack.On ? "On" : "Off";
            // a pack switched on joins at the top, losing to the ones already on; one switched off leaves the order
            _packs.Remove(row.Pack);
            var firstOn = _packs.FindIndex(p => p.CanSwitch && p.On);
            if (row.Pack.On) _packs.Insert(firstOn < 0 ? _packs.Count : firstOn, row.Pack);
            else _packs.Insert(Math.Max(0, _packs.FindIndex(p => p.CanSwitch)), row.Pack);
            Save();
            Draw();
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(((FrameworkElement)sender).Tag as PackRow, -1);
        private void Down_Click(object sender, RoutedEventArgs e) => Move(((FrameworkElement)sender).Tag as PackRow, 1);

        /// <summary>Swap with the next pack that is on, in that direction.</summary>
        private void Move(PackRow row, int dir)
        {
            if (row == null) return;
            var at = _packs.IndexOf(row.Pack);
            var to = at + dir;
            while (to >= 0 && to < _packs.Count && !(_packs[to].CanSwitch && _packs[to].On)) to += dir;
            if (to < 0 || to >= _packs.Count) return;
            var other = _packs[to];
            _packs[to] = row.Pack;
            _packs[at] = other;
            Save();
            Draw();
        }
    }
}
