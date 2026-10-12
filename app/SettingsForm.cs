using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiveBackground
{
    /// <summary>Sliders for every wallpaper setting. Changes apply to the desktop while dragging.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Settings settings;
        private readonly Action changed;
        private readonly TableLayoutPanel grid;
        private readonly Action[] refreshers = new Action[5];
        private int rows;

        public SettingsForm(Settings settings, Action changed)
        {
            this.settings = settings;
            this.changed = changed;

            Text = "Live Background settings";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, 16, 12); // left/top margin comes from the grid's location

            grid = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(16, 12),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Row("Mist speed", "How fast the fog drifts up the slopes (%).", 0, 300, 10,
                () => settings.Speed, v => settings.Speed = v);
            Row("Mist movement", "How much the fog thickens and thins. 0 is the still photo.", 0, 200, 10,
                () => settings.Thickness, v => settings.Thickness = v);
            Row("Drifting wisps", "The thin veil travelling between the tree rows.", 0, 100, 5,
                () => settings.Wisps, v => settings.Wisps = v);
            Row("Tree sway", "How far the treetops bend in the gusts. 0 keeps the trees still.", 0, 100, 5,
                () => settings.Sway, v => settings.Sway = v);
            Row("Frame rate", "Frames per second. Mist is slow, so 30 looks smooth.", 15, 60, 5,
                () => settings.Fps, v => settings.Fps = v);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            var done = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK };
            done.Click += (s, e) => Close();
            var reset = new Button { Text = "Reset to defaults", AutoSize = true };
            reset.Click += (s, e) => ResetDefaults();
            buttons.Controls.Add(done);
            buttons.Controls.Add(reset);
            grid.Controls.Add(buttons, 0, rows * 2);
            grid.SetColumnSpan(buttons, 3);

            Controls.Add(grid);
            AcceptButton = done;
            CancelButton = done;
        }

        private void Row(string title, string hint, int min, int max, int tick, Func<int> get, Action<int> set)
        {
            int index = rows++;
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 10, 12, 0),
            };
            var slider = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = tick,
                SmallChange = 1,
                LargeChange = tick,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 32,
                Margin = new Padding(0, 6, 8, 0),
            };
            var number = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Width = 64,
                TextAlign = HorizontalAlignment.Right,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0),
            };
            var note = new Label
            {
                Text = hint,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(410, 0), // wrap instead of widening the window
                Margin = new Padding(0, 0, 0, 6),
            };

            bool syncing = false;
            void Push(int v)
            {
                if (syncing) return;
                syncing = true;
                v = Math.Max(min, Math.Min(max, v));
                slider.Value = v;
                number.Value = v;
                syncing = false;
                if (v == get()) return;
                set(v);
                changed();
            }
            slider.ValueChanged += (s, e) => Push(slider.Value);
            number.ValueChanged += (s, e) => Push((int)number.Value);

            refreshers[index] = () =>
            {
                syncing = true;
                int v = Math.Max(min, Math.Min(max, get()));
                slider.Value = v;
                number.Value = v;
                syncing = false;
            };
            refreshers[index]();

            grid.Controls.Add(label, 0, index * 2);
            grid.Controls.Add(slider, 1, index * 2);
            grid.Controls.Add(number, 2, index * 2);
            grid.Controls.Add(note, 1, index * 2 + 1);
            grid.SetColumnSpan(note, 2);
        }

        private void ResetDefaults()
        {
            var d = new Settings();
            settings.Speed = d.Speed;
            settings.Thickness = d.Thickness;
            settings.Wisps = d.Wisps;
            settings.Sway = d.Sway;
            settings.Fps = d.Fps;
            foreach (var r in refreshers) r();
            changed();
        }
    }
}
