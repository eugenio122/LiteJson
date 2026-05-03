using System;
using System.Drawing;
using System.Windows.Forms;
using LiteJson.Core;
using LiteJson.Models;

namespace LiteJson.UI
{
    public class JsonSettingsUI : UserControl
    {
        private LiteJsonConfig _config;
        private FlowLayoutPanel _flowPanel;

        private Label _lblTitle;
        private Label _lblDesc;

        private Label _lblToggle;
        private CheckBox _chkEnabled;

        private Label _lblQueue;
        private NumericUpDown _numQueue;

        private Label _lblPath;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private Panel _pathPanel;

        private GroupBox _grpCategory;
        private RadioButton _rbWeb;
        private RadioButton _rbSap;

        private Button _btnApply;
        private Label _lblStatus;

        private bool _currentIsDark;

        public JsonSettingsUI(bool isDarkMode = false)
        {
            this.Size = new Size(500, 550);
            _config = LiteJsonConfig.Load();
            _currentIsDark = isDarkMode;

            InitializeComponents();
            ApplyTheme(_currentIsDark);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyTheme(_currentIsDark);
        }

        private void InitializeComponents()
        {
            _flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(15)
            };
            this.Controls.Add(_flowPanel);

            _lblTitle = new Label { Text = LiteJsonLanguageManager.GetString("SettingsTitle"), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
            _lblDesc = new Label { Text = LiteJsonLanguageManager.GetString("SettingsDesc"), AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 20) };

            _lblToggle = new Label { Text = LiteJsonLanguageManager.GetString("EngineToggle"), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _chkEnabled = new CheckBox
            {
                Text = _config.IsEnabled ? LiteJsonLanguageManager.GetString("EngineOn") : LiteJsonLanguageManager.GetString("EngineOff"),
                Checked = _config.IsEnabled,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(150, 30),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 5, 0, 20),
                Cursor = Cursors.Hand
            };
            _chkEnabled.FlatAppearance.BorderSize = 1;
            UpdateToggleColor();
            _chkEnabled.CheckedChanged += (s, e) => UpdateToggleColor();

            _grpCategory = new GroupBox { Text = LiteJsonLanguageManager.GetString("TargetEngine"), Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 20), AutoSize = true };
            var rbFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(10) };
            _rbWeb = new RadioButton { Text = LiteJsonLanguageManager.GetString("WebUniversal"), AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 5, 0, 10), Checked = _config.Target == TargetEngine.WebUniversal };
            _rbSap = new RadioButton { Text = LiteJsonLanguageManager.GetString("SapEnterprise"), AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 10), Checked = _config.Target == TargetEngine.SapEnterprise };
            rbFlow.Controls.Add(_rbWeb); rbFlow.Controls.Add(_rbSap);
            _grpCategory.Controls.Add(rbFlow);

            // --- NOVO: Campo de Limite da Fila ---
            _lblQueue = new Label { Text = LiteJsonLanguageManager.GetString("QueueLimit"), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 5) };
            _numQueue = new NumericUpDown
            {
                Value = _config.MaxHydrationQueueSize,
                Minimum = 1,
                Maximum = 50,
                Width = 80,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 20)
            };

            _lblPath = new Label { Text = LiteJsonLanguageManager.GetString("OutputPath"), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 5) };
            _pathPanel = new Panel { Size = new Size(400, 30), Margin = new Padding(0, 0, 0, 25) };
            _txtPath = new TextBox { Text = _config.CustomOutputPath, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) };
            _btnBrowse = new Button { Text = LiteJsonLanguageManager.GetString("BrowseBtn"), Dock = DockStyle.Right, Width = 80, FlatStyle = FlatStyle.Flat };
            _btnBrowse.Click += BtnBrowse_Click;
            _pathPanel.Controls.Add(_txtPath);
            _pathPanel.Controls.Add(_btnBrowse);

            _btnApply = new Button { Text = LiteJsonLanguageManager.GetString("SaveBtn"), Size = new Size(150, 35), BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 10) };
            _btnApply.FlatAppearance.BorderSize = 0;
            _btnApply.Click += BtnApply_Click;

            _lblStatus = new Label { Text = "", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Italic) };

            _flowPanel.Controls.Add(_lblTitle);
            _flowPanel.Controls.Add(_lblDesc);
            _flowPanel.Controls.Add(_lblToggle);
            _flowPanel.Controls.Add(_chkEnabled);
            _flowPanel.Controls.Add(_grpCategory);
            _flowPanel.Controls.Add(_lblQueue);
            _flowPanel.Controls.Add(_numQueue);
            _flowPanel.Controls.Add(_lblPath);
            _flowPanel.Controls.Add(_pathPanel);
            _flowPanel.Controls.Add(_btnApply);
            _flowPanel.Controls.Add(_lblStatus);
        }

        private void UpdateToggleColor()
        {
            if (_chkEnabled == null) return;

            if (_chkEnabled.Checked)
            {
                _chkEnabled.Text = LiteJsonLanguageManager.GetString("EngineOn");
                _chkEnabled.BackColor = Color.SeaGreen;
                _chkEnabled.ForeColor = Color.White;
            }
            else
            {
                _chkEnabled.Text = LiteJsonLanguageManager.GetString("EngineOff");
                _chkEnabled.BackColor = Color.Gray;
                _chkEnabled.ForeColor = Color.White;
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog { Description = LiteJsonLanguageManager.GetString("OutputPath") })
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                    _txtPath.Text = fbd.SelectedPath;
            }
        }

        public void ApplyTheme(bool isDark)
        {
            _currentIsDark = isDark;
            Color backColor = isDark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(240, 240, 240);
            Color foreColor = isDark ? Color.White : Color.Black;

            this.BackColor = backColor;
            this.ForeColor = foreColor;

            ApplyThemeToControls(this.Controls, isDark);

            if (_lblTitle != null) { _lblTitle.ForeColor = foreColor; _lblTitle.BackColor = Color.Transparent; }
            if (_lblDesc != null) { _lblDesc.ForeColor = foreColor; _lblDesc.BackColor = Color.Transparent; }
            if (_lblToggle != null) { _lblToggle.ForeColor = foreColor; _lblToggle.BackColor = Color.Transparent; }
            if (_lblQueue != null) { _lblQueue.ForeColor = foreColor; _lblQueue.BackColor = Color.Transparent; }
            if (_lblPath != null) { _lblPath.ForeColor = foreColor; _lblPath.BackColor = Color.Transparent; }

            if (_grpCategory != null) { _grpCategory.ForeColor = foreColor; _grpCategory.BackColor = backColor; }
            if (_rbWeb != null) { _rbWeb.ForeColor = foreColor; _rbWeb.BackColor = Color.Transparent; }
            if (_rbSap != null) { _rbSap.ForeColor = foreColor; _rbSap.BackColor = Color.Transparent; }

            if (_txtPath != null)
            {
                _txtPath.BackColor = isDark ? Color.FromArgb(30, 30, 30) : Color.White;
                _txtPath.ForeColor = foreColor;
            }

            if (_numQueue != null)
            {
                _numQueue.BackColor = isDark ? Color.FromArgb(30, 30, 30) : Color.White;
                _numQueue.ForeColor = foreColor;
            }

            UpdateToggleColor();
            if (_btnApply != null) { _btnApply.BackColor = Color.FromArgb(0, 120, 215); _btnApply.ForeColor = Color.White; }
            if (_btnBrowse != null)
            {
                _btnBrowse.BackColor = isDark ? Color.FromArgb(60, 60, 60) : Color.LightGray;
                _btnBrowse.ForeColor = foreColor;
            }
            if (_lblStatus != null) { _lblStatus.ForeColor = isDark ? Color.LightGreen : Color.Green; _lblStatus.BackColor = Color.Transparent; }
        }

        private void ApplyThemeToControls(Control.ControlCollection controls, bool isDark)
        {
            Color backColor = isDark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(240, 240, 240);
            Color foreColor = isDark ? Color.White : Color.Black;

            foreach (Control ctrl in controls)
            {
                if (ctrl == _chkEnabled || ctrl == _btnApply || ctrl == _btnBrowse || ctrl == _lblStatus)
                    continue;

                if (ctrl is Label || ctrl is CheckBox || ctrl is RadioButton)
                {
                    ctrl.ForeColor = foreColor;
                    ctrl.BackColor = Color.Transparent;
                }
                else if (ctrl is Panel || ctrl is FlowLayoutPanel || ctrl is GroupBox)
                {
                    ctrl.BackColor = backColor;
                    if (ctrl is GroupBox) ctrl.ForeColor = foreColor;
                    ApplyThemeToControls(ctrl.Controls, isDark);
                }
                else if (ctrl is TextBox || ctrl is NumericUpDown)
                {
                    ctrl.BackColor = isDark ? Color.FromArgb(30, 30, 30) : Color.White;
                    ctrl.ForeColor = foreColor;
                }
            }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            _config.IsEnabled = _chkEnabled.Checked;
            _config.Target = _rbWeb.Checked ? TargetEngine.WebUniversal : TargetEngine.SapEnterprise;
            _config.CustomOutputPath = _txtPath.Text.Trim();
            _config.MaxHydrationQueueSize = (int)_numQueue.Value;

            _config.Save();

            _lblStatus.Text = LiteJsonLanguageManager.GetString("SavedStatus");
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (s, ev) => { _lblStatus.Text = ""; timer.Stop(); timer.Dispose(); };
            timer.Start();
        }
    }
}