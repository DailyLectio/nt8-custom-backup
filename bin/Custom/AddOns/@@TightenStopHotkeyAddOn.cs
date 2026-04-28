#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class TightenStopHotkeyAddOn : AddOnBase
    {
        // --- Control Center menu (optional) ---
        private NTMenuItem newMenu;
        private NTMenuItem hotkeysMenuItem;

        // --- Track charts we’ve modified ---
        private readonly Dictionary<Chart, KeyEventHandler> chartKeyHandlers = new Dictionary<Chart, KeyEventHandler>();
        private readonly Dictionary<Chart, Grid> overlayGrids = new Dictionary<Chart, Grid>();
        private readonly Dictionary<Chart, TextBlock> overlayText = new Dictionary<Chart, TextBlock>();
        private readonly Dictionary<Chart, Button> armButtons = new Dictionary<Chart, Button>();
        private readonly Dictionary<Chart, Button> tightenButtons = new Dictionary<Chart, Button>();

        // --- Armed chart state (single chart at a time) ---
        private Chart armedChart = null;
        private string armedInstrument = null;

        protected override void OnStateChange()
        {
            if (State == State.Active)
                Print("TightenStopHotkeyAddOn ACTIVE");
        }

        protected override void OnWindowCreated(Window window)
        {
            // 1) Control Center: add a menu item (not required, but handy)
            var cc = window as ControlCenter;
            if (cc != null)
            {
                newMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
                if (newMenu != null && hotkeysMenuItem == null)
                {
                    hotkeysMenuItem = new NTMenuItem
                    {
                        Header = "Stop Hotkeys (Chart Arm)",
                        Style = Application.Current.TryFindResource("MainMenuItem") as Style
                    };
                    hotkeysMenuItem.Click += (s, e) =>
                    {
                        Print("Stop Hotkeys AddOn running. Arm a chart to enable Ctrl+Shift+T.");
                    };
                    newMenu.Items.Add(hotkeysMenuItem);
                }
                return;
            }

            // 2) Chart windows: attach overlay + hotkey handler
            var chart = window as Chart;
            if (chart == null)
                return;

            // Build overlay safely on that chart’s UI thread
            chart.Dispatcher.BeginInvoke(new Action(() =>
            {
                TryInstallOverlay(chart);
                HookHotkey(chart);
                RefreshOverlay(chart);
            }));
        }

        protected override void OnWindowDestroyed(Window window)
        {
            // Remove CC menu item
            if (window is ControlCenter && hotkeysMenuItem != null)
            {
                try
                {
                    if (newMenu != null && newMenu.Items.Contains(hotkeysMenuItem))
                        newMenu.Items.Remove(hotkeysMenuItem);
                }
                catch { }

                hotkeysMenuItem = null;
                return;
            }

            // Cleanup chart hooks/overlay
            var chart = window as Chart;
            if (chart == null)
                return;

            try
            {
                if (chartKeyHandlers.TryGetValue(chart, out var handler))
                {
                    chart.PreviewKeyDown -= handler;
                    chartKeyHandlers.Remove(chart);
                }

                if (overlayGrids.TryGetValue(chart, out var grid))
                {
                    var root = chart.Content as Grid;
                    if (root != null)
                        root.Children.Remove(grid);

                    overlayGrids.Remove(chart);
                    overlayText.Remove(chart);
                    armButtons.Remove(chart);
                    tightenButtons.Remove(chart);
                }

                if (armedChart == chart)
                {
                    armedChart = null;
                    armedInstrument = null;
                }
            }
            catch { }
        }

        private void HookHotkey(Chart chart)
        {
            if (chartKeyHandlers.ContainsKey(chart))
                return;

            KeyEventHandler handler = (s, e) =>
            {
                try
                {
                    // Only act if a chart is armed
                    if (armedChart == null)
                        return;

                    // Hotkey
                    if (e.Key == Key.T &&
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        FireTightenFromArmedChart();
                        e.Handled = true;
                    }
                }
                catch (Exception ex)
                {
                    Print("Hotkey handler error: " + ex);
                }
            };

            chartKeyHandlers[chart] = handler;
            chart.PreviewKeyDown += handler;
        }

        private void TryInstallOverlay(Chart chart)
        {
            if (overlayGrids.ContainsKey(chart))
                return;

            var root = chart.Content as Grid;
            if (root == null)
                return;

            // Overlay container
            var bar = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 8, 0, 0),
                Width = 520,
                Height = 38,
                IsHitTestVisible = true
            };

            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); // ARM
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // TIGHTEN
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // TEXT

            // ARM button
            var armBtn = new Button
            {
                Content = "ARM THIS CHART",
                Margin = new Thickness(6, 5, 6, 5),
                Padding = new Thickness(8, 2, 8, 2),
                FontWeight = FontWeights.SemiBold
            };
            armBtn.Click += (s, e) =>
            {
                ArmChart(chart);
                // Give focus to chart window so hotkey is more likely to work
                try { chart.Activate(); chart.Focus(); } catch { }
            };
            Grid.SetColumn(armBtn, 0);
            bar.Children.Add(armBtn);

            // TIGHTEN button
            var tBtn = new Button
            {
                Content = "TIGHTEN STOP",
                Margin = new Thickness(6, 5, 6, 5),
                Padding = new Thickness(8, 2, 8, 2),
                FontWeight = FontWeights.SemiBold
            };
            tBtn.Click += (s, e) =>
            {
                FireTightenFromArmedChart();
            };
            Grid.SetColumn(tBtn, 1);
            bar.Children.Add(tBtn);

            // Status text
            var txt = new TextBlock
            {
                Foreground = Brushes.Gold,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(txt, 2);
            bar.Children.Add(txt);

            // Add to chart root
            root.Children.Add(bar);

            overlayGrids[chart] = bar;
            overlayText[chart] = txt;
            armButtons[chart] = armBtn;
            tightenButtons[chart] = tBtn;
        }

        private void ArmChart(Chart chart)
        {
            armedChart = chart;
            armedInstrument = chart.ActiveChartControl?.Instrument?.FullName;

            Print($"ARMED chart: {(armedInstrument ?? "UNKNOWN")}");

            // Refresh ALL overlays so only one shows ARMED
            foreach (var kv in overlayGrids)
            {
                var c = kv.Key;
                try
                {
                    c.Dispatcher.BeginInvoke(new Action(() => RefreshOverlay(c)));
                }
                catch { }
            }
        }

        private void RefreshOverlay(Chart chart)
        {
            if (!overlayText.TryGetValue(chart, out var txt) || txt == null)
                return;

            string inst = chart.ActiveChartControl?.Instrument?.FullName ?? "UNKNOWN";
            bool isArmed = (armedChart == chart);

            txt.Text = isArmed
                ? $"STOP HOTKEY: ARMED | {inst} | Ctrl+Shift+T or Button"
                : $"STOP HOTKEY: DISARMED | {inst} | Click ARM THIS CHART";

            // Also visually disable tighten button unless armed
            if (tightenButtons.TryGetValue(chart, out var btn) && btn != null)
                btn.IsEnabled = isArmed;
        }

        private void FireTightenFromArmedChart()
        {
            if (armedChart == null)
            {
                Print("TIGHTEN ignored: no chart is armed.");
                return;
            }

            string inst = armedChart.ActiveChartControl?.Instrument?.FullName;

            if (string.IsNullOrEmpty(inst))
            {
                Print("TIGHTEN ignored: armed chart instrument was UNKNOWN.");
                return;
            }

            // Fire bus (strategy consumes it and tightens stop)
            NinjaTrader.NinjaScript.TightenBus.FireTighten(inst);

            Print($"TIGHTEN FIRED -> {inst}");

            // Update overlay timestamp on the armed chart
            try
            {
                armedChart.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (overlayText.TryGetValue(armedChart, out var txt) && txt != null)
                        txt.Text = $"STOP HOTKEY: ARMED | {inst} | FIRED @ {DateTime.Now:HH:mm:ss}";
                }));
            }
            catch { }
        }
    }
}
