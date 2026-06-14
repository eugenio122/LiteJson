using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteJson.Cleaner;
using LiteJson.Diagnostics;

namespace LiteJson.Cleaner
{
    /// <summary>
    /// Painel desacoplado para a limpeza offline do Scenario_Data.json.
    /// O usuário importa o JSON completo, o serviço gera a versão clean e
    /// o usuário escolhe onde exportar. Não interage com o pipeline de captura.
    /// </summary>
    public class JsonCleanerUI : UserControl
    {
        private readonly JsonCleanerService _service = new JsonCleanerService();

        private Label _lblTitle;
        private Label _lblDesc;
        private Button _btnImport;
        private Label _lblStatus;
        private ProgressBar _progress;

        private string _inputPath;

        public JsonCleanerUI(bool isDarkMode = false)
        {
            this.Size = new Size(500, 300);
            InitializeComponents();
            ApplyTheme(isDarkMode);
        }

        private void InitializeComponents()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(15)
            };
            this.Controls.Add(flow);

            _lblTitle = new Label
            {
                Text = "Limpeza de JSON para IA",
                AutoSize = true,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };

            _lblDesc = new Label
            {
                Text = "Importe o Scenario_Data.json completo para gerar uma versão enxuta " +
                       "(somente steps e trilha de interações com seletores BiDi), ideal para consumo por IA.",
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 0, 0, 20)
            };

            _btnImport = new Button
            {
                Text = "Importar e Limpar JSON...",
                Size = new Size(220, 38),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 15)
            };
            _btnImport.FlatAppearance.BorderSize = 0;
            _btnImport.Click += BtnImport_Click;

            _progress = new ProgressBar
            {
                Size = new Size(450, 18),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false,
                Margin = new Padding(0, 0, 0, 10)
            };

            _lblStatus = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Italic)
            };

            flow.Controls.Add(_lblTitle);
            flow.Controls.Add(_lblDesc);
            flow.Controls.Add(_btnImport);
            flow.Controls.Add(_progress);
            flow.Controls.Add(_lblStatus);
        }

        private async void BtnImport_Click(object sender, EventArgs e)
        {
            // 1. Escolher o arquivo de entrada
            using (var ofd = new OpenFileDialog
            {
                Title = "Selecione o Scenario_Data.json completo",
                Filter = "Arquivos JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                _inputPath = ofd.FileName;
            }

            // 2. Escolher onde salvar a saída
            string outputPath;
            using (var sfd = new SaveFileDialog
            {
                Title = "Salvar JSON limpo como...",
                Filter = "Arquivos JSON (*.json)|*.json",
                FileName = "Scenario_Clean.json"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                outputPath = sfd.FileName;
            }

            // 3. Processar com loading
            SetBusy(true);
            _lblStatus.Text = "Processando...";

            try
            {
                int count = await Task.Run(() => _service.CleanFile(_inputPath, outputPath));
                _lblStatus.ForeColor = Color.Green;
                _lblStatus.Text = $"Concluído! {count} steps exportados para:\n{outputPath}";
            }
            catch (Exception ex)
            {
                LiteLogger.Error("[JsonCleanerUI] Falha ao limpar o JSON.", ex);
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = $"Erro: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _progress.Visible = busy;
            _btnImport.Enabled = !busy;
        }

        public void ApplyTheme(bool isDark)
        {
            Color backColor = isDark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(240, 240, 240);
            Color foreColor = isDark ? Color.White : Color.Black;

            this.BackColor = backColor;
            this.ForeColor = foreColor;

            foreach (Control ctrl in this.Controls)
                ApplyThemeRecursive(ctrl, backColor, foreColor, isDark);
        }

        private void ApplyThemeRecursive(Control ctrl, Color backColor, Color foreColor, bool isDark)
        {
            if (ctrl is Button) return; // o botão tem cor própria

            if (ctrl is Label)
            {
                ctrl.ForeColor = ctrl == _lblStatus ? ctrl.ForeColor : foreColor;
                ctrl.BackColor = Color.Transparent;
            }
            else
            {
                ctrl.BackColor = backColor;
                ctrl.ForeColor = foreColor;
            }

            foreach (Control child in ctrl.Controls)
                ApplyThemeRecursive(child, backColor, foreColor, isDark);
        }
    }
}