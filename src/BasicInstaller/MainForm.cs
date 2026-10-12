using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BasicInstaller.Models;
using BasicInstaller.Services;

namespace BasicInstaller
{
    /// <summary>
    /// Hauptfenster: Tabelle der Katalog-Apps (Checkbox, installierte Version,
    /// Sprache, Pfad, Lizenz/Optionen) + Installieren-Button + Protokoll.
    /// Bewusst eine einzige, flache Ansicht statt mehrerer Dialoge/Tabs - alles
    /// Relevante ist auf den ersten Blick sichtbar. Kontaktdaten und
    /// App-Update laufen ueber kleine Buttons/Popups, damit das Hauptfenster
    /// clean bleibt (siehe README "UX").
    /// </summary>
    public class MainForm : Form
    {
        private BindingList<AppRow> _rows;

        private DataGridView _grid;
        private DataGridViewCheckBoxColumn _colSelected;
        private DataGridViewTextBoxColumn _colName;
        private DataGridViewTextBoxColumn _colCategory;
        private DataGridViewTextBoxColumn _colInstalled;
        private DataGridViewComboBoxColumn _colLanguage;
        private DataGridViewTextBoxColumn _colPath;
        private DataGridViewTextBoxColumn _colLicense;
        private DataGridViewButtonColumn _colOptions;

        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Button _btnRefreshStatus;
        private Button _btnPreload;
        private Button _btnCheckUpdate;
        private ComboBox _cmbProfiles;
        private Button _btnSaveProfile;
        private Button _btnDeleteProfile;
        private CheckBox _chkSkipInstalled;
        private Button _btnInstall;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Label _lblElevationWarning;
        private LinkLabel _lnkAbout;
        private Label _lblFooter;
        private TextBox _txtLog;

        private bool _busy;

        public MainForm()
        {
            BuildUi();
            LoadCatalogAndConfig();
            UpdateElevationBanner();
        }

        // ---------------------------------------------------------------
        // UI-Aufbau
        // ---------------------------------------------------------------

        private void BuildUi()
        {
            Text = "Basic Software Installer";
            MinimumSize = new Size(980, 680);
            Size = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico")); }
            catch { /* Icon ist rein kosmetisch - ein fehlendes app.ico darf den Start nicht verhindern */ }

            _lblElevationWarning = new Label
            {
                Dock = DockStyle.Top,
                Text = "⚠ Nicht als Administrator gestartet - Installationen könnten fehlschlagen.",
                BackColor = Color.FromArgb(255, 244, 204),
                ForeColor = Color.FromArgb(140, 40, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 26,
                Visible = false,
            };

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(12, 10, 12, 6),
            };
            header.Controls.Add(new Label
            {
                Text = "Basic Software Installer",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                AutoSize = true,
            });
            header.Controls.Add(new Label
            {
                Text = "Programme auswählen, bei Bedarf Pfad/Sprache/Optionen anpassen, dann unten auf „Installieren“ klicken. " +
                       "Bereits installierte Programme werden dabei aktualisiert.",
                ForeColor = Color.Gray,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0),
            });

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(12, 0, 12, 6),
            };
            _btnSelectAll = new Button { Text = "Alle auswählen", AutoSize = true };
            _btnSelectNone = new Button { Text = "Keine auswählen", AutoSize = true };
            _btnRefreshStatus = new Button { Text = "🔄 Status aktualisieren", AutoSize = true, Margin = new Padding(16, 0, 0, 0) };
            _btnPreload = new Button { Text = "⬇ Nur herunterladen (Cache)", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            _btnCheckUpdate = new Button { Text = "🔄 Nach App-Updates suchen", AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
            _btnSelectAll.Click += (s, e) => SetAllSelected(true);
            _btnSelectNone.Click += (s, e) => SetAllSelected(false);
            _btnRefreshStatus.Click += (s, e) => RefreshAllInstalledVersions();
            _btnPreload.Click += BtnPreload_Click;
            _btnCheckUpdate.Click += BtnCheckUpdate_Click;
            toolbar.Controls.Add(_btnSelectAll);
            toolbar.Controls.Add(_btnSelectNone);
            toolbar.Controls.Add(_btnRefreshStatus);
            toolbar.Controls.Add(_btnPreload);
            toolbar.Controls.Add(_btnCheckUpdate);

            var profileBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(12, 0, 12, 6),
            };
            profileBar.Controls.Add(new Label { Text = "Profil:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _cmbProfiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            _btnSaveProfile = new Button { Text = "💾 Speichern als…", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            _btnDeleteProfile = new Button { Text = "🗑 Löschen", AutoSize = true, Margin = new Padding(4, 0, 0, 0), Enabled = false };
            _cmbProfiles.SelectedIndexChanged += CmbProfiles_SelectedIndexChanged;
            _btnSaveProfile.Click += BtnSaveProfile_Click;
            _btnDeleteProfile.Click += BtnDeleteProfile_Click;
            profileBar.Controls.Add(_cmbProfiles);
            profileBar.Controls.Add(_btnSaveProfile);
            profileBar.Controls.Add(_btnDeleteProfile);
            RefreshProfileList(selectName: null);

            BuildGrid();

            var optionsBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(12, 4, 12, 0),
            };
            _chkSkipInstalled = new CheckBox
            {
                Text = "Bereits installierte Programme überspringen (kein erneuter Download/Install)",
                AutoSize = true,
                Checked = true,
            };
            optionsBar.Controls.Add(_chkSkipInstalled);

            var installBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 4),
            };
            installBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            installBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 22 };
            _btnInstall = new Button
            {
                Text = "Installieren",
                AutoSize = true,
                Height = 30,
                Width = 140,
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            _btnInstall.Click += BtnInstall_Click;
            installBar.Controls.Add(_progressBar, 0, 0);
            installBar.Controls.Add(_btnInstall, 1, 0);

            _lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Text = "Bereit. Programme auswählen und auf „Installieren“ klicken.",
                Padding = new Padding(12, 0, 12, 4),
                AutoSize = false,
                Height = 22,
            };

            var logPanel = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(12, 0, 12, 8) };
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5f),
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle,
            };
            logPanel.Controls.Add(_txtLog);

            var footerBar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(12, 2, 12, 4),
            };
            footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _lnkAbout = new LinkLabel { Text = "ℹ Über / Kontakt", AutoSize = true };
            _lnkAbout.LinkClicked += (s, e) => ShowAboutDialog();
            _lblFooter = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.Gray,
                Text = BuildFooterText(),
            };
            footerBar.Controls.Add(_lnkAbout, 0, 0);
            footerBar.Controls.Add(_lblFooter, 1, 0);

            // Explizites TableLayoutPanel mit fester Zeilenzuordnung statt
            // mehrerer Dock=Top-Controls: bei gleichem Dock-Wert haengt die
            // tatsaechliche Stapel-Reihenfolge in WinForms von der Hinzufuege-
            // Reihenfolge ab und ist von hier aus (Linux-Build, keine
            // Windows-GUI zum visuellen Pruefen) nicht zuverlaessig
            // verifizierbar - die Zeilennummer macht die Reihenfolge dagegen
            // unzweideutig.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 0: Admin-Warnung
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 1: Titel
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 2: Toolbar
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 3: Profilzeile
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 4: Grid
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 5: Optionen (Skip-Checkbox)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 6: Installieren-Leiste
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 7: Statuszeile
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); // 8: Protokoll
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 9: Footer

            _lblElevationWarning.Dock = DockStyle.Fill;
            header.Dock = DockStyle.Fill;
            toolbar.Dock = DockStyle.Fill;
            profileBar.Dock = DockStyle.Fill;
            optionsBar.Dock = DockStyle.Fill;
            installBar.Dock = DockStyle.Fill;
            _lblStatus.Dock = DockStyle.Fill;
            logPanel.Dock = DockStyle.Fill;
            footerBar.Dock = DockStyle.Fill;

            root.Controls.Add(_lblElevationWarning, 0, 0);
            root.Controls.Add(header, 0, 1);
            root.Controls.Add(toolbar, 0, 2);
            root.Controls.Add(profileBar, 0, 3);
            root.Controls.Add(_grid, 0, 4);
            root.Controls.Add(optionsBar, 0, 5);
            root.Controls.Add(installBar, 0, 6);
            root.Controls.Add(_lblStatus, 0, 7);
            root.Controls.Add(logPanel, 0, 8);
            root.Controls.Add(footerBar, 0, 9);

            Controls.Add(root);

            FormClosing += (s, e) => SaveUserConfig();
            Shown += async (s, e) => await OfferUpdateIfAvailableAsync(interactive: false);
        }

        private void BuildGrid()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
            };

            _colSelected = new DataGridViewCheckBoxColumn
            {
                DataPropertyName = "Selected",
                HeaderText = "",
                Width = 32,
                Resizable = DataGridViewTriState.False,
            };
            _colName = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Name",
                HeaderText = "Programm",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 130,
            };
            _colCategory = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Category",
                HeaderText = "Kategorie",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 85,
            };
            _colInstalled = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "InstalledVersion",
                HeaderText = "Installiert",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 75,
                ToolTipText = "Aus der Windows-Uninstall-Registry erkannte Version, '–' = nicht gefunden.",
            };
            _colLanguage = new DataGridViewComboBoxColumn
            {
                DataPropertyName = "SelectedLanguage",
                HeaderText = "Sprache",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 70,
                FlatStyle = FlatStyle.Flat,
                ToolTipText = "Nur wählbar, wenn das Programm im Katalog Sprachvarianten hinterlegt hat.",
            };
            _colPath = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "InstallPath",
                HeaderText = "Installationspfad",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 170,
                ToolTipText = "Nur änderbar, wenn der Installer einen Pfad-Switch unterstützt (siehe Katalog).",
            };
            _colLicense = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "LicenseKey",
                HeaderText = "Lizenzschlüssel",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 120,
                ToolTipText = "Optional - leer lassen, um ohne Aktivierung zu installieren und später zu aktivieren. Wird nie gespeichert.",
            };
            _colOptions = new DataGridViewButtonColumn
            {
                HeaderText = "",
                Text = "⚙",
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.ColumnHeader,
                Width = 36,
                ToolTipText = "Installationsoptionen (z.B. Kontextmenü-Integration, Dateizuordnungen) - nur sichtbar wenn verfügbar.",
            };

            _grid.Columns.AddRange(_colSelected, _colName, _colCategory, _colInstalled, _colLanguage, _colPath, _colLicense, _colOptions);
            _grid.DataBindingComplete += Grid_DataBindingComplete;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CellClick += Grid_CellClick;
        }

        private string BuildFooterText()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"© {DateTime.Now.Year} AS Computertechnik  –  v{version.Major}.{version.Minor}.{version.Build}";
        }

        private void ShowAboutDialog()
        {
            // Kontaktdaten absichtlich NICHT im Hauptfenster, sondern nur hier -
            // das Hauptfenster soll clean/fokussiert bleiben (Nutzer-Vorgabe).
            using (var about = new AboutDialog())
                about.ShowDialog(this);
        }

        // ---------------------------------------------------------------
        // Katalog / Konfiguration
        // ---------------------------------------------------------------

        private void LoadCatalogAndConfig()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var entries = CatalogLoader.Load(baseDir)
                .OrderBy(a => a.category)
                .ThenBy(a => a.name)
                .ToArray();

            var savedConfig = ConfigStore.Load();

            _rows = new BindingList<AppRow>();
            foreach (var entry in entries)
            {
                var row = new AppRow(entry);
                if (savedConfig.apps.TryGetValue(entry.id, out var saved))
                {
                    row.Selected = saved.selected;
                    if (!string.IsNullOrWhiteSpace(saved.install_path))
                        row.InstallPath = saved.install_path;
                    if (!string.IsNullOrWhiteSpace(saved.language))
                        row.SelectedLanguage = saved.language;
                    if (saved.options != null)
                        foreach (var kv in saved.options)
                            row.Options[kv.Key] = kv.Value;
                    // Lizenzschluessel/-datei bewusst nicht aus der Config geladen -
                    // siehe ConfigStore.AppUserSettings; Volumenlizenzen koennen
                    // optional ueber den Windows Credential Manager vorbelegt werden
                    // (WindowsCredentialStore), niemals ueber die Klartext-config.json.
                    var storedSecret = WindowsCredentialStore.TryRead(WindowsCredentialStore.KeyForApp(entry.id));
                    if (!string.IsNullOrEmpty(storedSecret))
                        row.LicenseKey = storedSecret;
                }
                _rows.Add(row);
            }

            _grid.DataSource = _rows;
            UpdateInstallButtonEnabled();
        }

        private void SaveUserConfig() => ConfigStore.Save(BuildCurrentUserConfig());

        /// <summary>
        /// Momentaufnahme der aktuellen Auswahl/Pfade/Sprachen/Optionen - Basis
        /// sowohl fuer config.json (automatisch bei jedem Beenden) als auch fuer
        /// benannte Profile (explizit per "Speichern als..."). Enthaelt bewusst
        /// NIE Lizenzschluessel/-dateien, weil AppUserSettings dafuer kein Feld
        /// hat - gilt dadurch automatisch auch fuer Profile.
        /// </summary>
        private UserConfig BuildCurrentUserConfig()
        {
            var cfg = new UserConfig();
            foreach (var row in _rows)
            {
                cfg.apps[row.Source.id] = new AppUserSettings
                {
                    selected = row.Selected,
                    install_path = row.PathEditable ? row.InstallPath : null,
                    language = row.LanguageSelectable ? row.SelectedLanguage : null,
                    options = row.Options.Count > 0 ? new Dictionary<string, bool>(row.Options) : null,
                };
            }
            return cfg;
        }

        /// <summary>
        /// Wendet einen UserConfig-Snapshot (Profil) auf die bereits bestehenden
        /// Zeilen an. Im Gegensatz zum initialen Laden in LoadCatalogAndConfig
        /// wird hier eine App, die im Profil fehlt, aktiv ABgewaehlt - ein
        /// Profil beschreibt einen vollstaendigen Zustand, kein additives Merge.
        /// </summary>
        private void ApplyUserConfig(UserConfig cfg)
        {
            foreach (var row in _rows)
            {
                if (cfg.apps.TryGetValue(row.Source.id, out var saved))
                {
                    row.Selected = saved.selected;
                    if (!string.IsNullOrWhiteSpace(saved.install_path))
                        row.InstallPath = saved.install_path;
                    if (!string.IsNullOrWhiteSpace(saved.language))
                        row.SelectedLanguage = saved.language;
                    if (saved.options != null)
                        foreach (var kv in saved.options)
                            row.Options[kv.Key] = kv.Value;
                }
                else
                {
                    row.Selected = false;
                }
            }

            // Einfachster robuster Weg, die Grid-Zellen nach Aenderungen an den
            // gebundenen Objekten neu zu rendern (BindingList meldet reine
            // Property-Aenderungen sonst nicht automatisch ans DataGridView).
            _grid.DataSource = null;
            _grid.DataSource = _rows;
            UpdateInstallButtonEnabled();
        }

        private void RefreshProfileList(string selectName)
        {
            _cmbProfiles.SelectedIndexChanged -= CmbProfiles_SelectedIndexChanged;
            _cmbProfiles.Items.Clear();
            _cmbProfiles.Items.Add("– Profil wählen –");
            foreach (var name in ProfileStore.LoadAll().Keys.OrderBy(n => n))
                _cmbProfiles.Items.Add(name);

            var indexToSelect = selectName != null ? _cmbProfiles.Items.IndexOf(selectName) : -1;
            _cmbProfiles.SelectedIndex = indexToSelect >= 0 ? indexToSelect : 0;
            _btnDeleteProfile.Enabled = _cmbProfiles.SelectedIndex > 0;
            _cmbProfiles.SelectedIndexChanged += CmbProfiles_SelectedIndexChanged;
        }

        private void CmbProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            _btnDeleteProfile.Enabled = _cmbProfiles.SelectedIndex > 0;
            if (_cmbProfiles.SelectedIndex <= 0 || _rows == null) return;

            var name = (string)_cmbProfiles.SelectedItem;
            var all = ProfileStore.LoadAll();
            if (all.TryGetValue(name, out var cfg))
            {
                ApplyUserConfig(cfg);
                _lblStatus.Text = $"Profil „{name}“ geladen.";
            }
        }

        private void BtnSaveProfile_Click(object sender, EventArgs e)
        {
            var currentName = _cmbProfiles.SelectedIndex > 0 ? (string)_cmbProfiles.SelectedItem : "";
            using (var prompt = new PromptDialog("Profil speichern", "Name des Profils:", currentName))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.Value))
                    return;

                ProfileStore.Save(prompt.Value, BuildCurrentUserConfig());
                RefreshProfileList(prompt.Value);
                _lblStatus.Text = $"Profil „{prompt.Value}“ gespeichert.";
            }
        }

        private void BtnDeleteProfile_Click(object sender, EventArgs e)
        {
            if (_cmbProfiles.SelectedIndex <= 0) return;
            var name = (string)_cmbProfiles.SelectedItem;

            var answer = MessageBox.Show(this, $"Profil „{name}“ wirklich löschen?", "Basic Installer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            ProfileStore.Delete(name);
            RefreshProfileList(null);
            _lblStatus.Text = $"Profil „{name}“ gelöscht.";
        }

        private void Grid_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!(gridRow.DataBoundItem is AppRow row)) continue;

                var langCell = (DataGridViewComboBoxCell)gridRow.Cells[_colLanguage.Index];
                langCell.Items.Clear();
                langCell.Items.AddRange(row.AvailableLanguages);
                langCell.Value = row.SelectedLanguage;
                if (!row.LanguageSelectable)
                {
                    gridRow.Cells[_colLanguage.Index].ReadOnly = true;
                    gridRow.Cells[_colLanguage.Index].Style.BackColor = SystemColors.Control;
                }

                if (!row.PathEditable)
                {
                    gridRow.Cells[_colPath.Index].ReadOnly = true;
                    gridRow.Cells[_colPath.Index].Style.BackColor = SystemColors.Control;
                    gridRow.Cells[_colPath.Index].Value = "(kein Pfad-Override möglich)";
                }

                if (!row.LicenseKeyEditable)
                {
                    gridRow.Cells[_colLicense.Index].ReadOnly = true;
                    gridRow.Cells[_colLicense.Index].Style.BackColor = SystemColors.Control;
                    gridRow.Cells[_colLicense.Index].Value = "(nicht erforderlich)";
                }
                else if (!string.IsNullOrEmpty(row.LicenseKey))
                {
                    gridRow.Cells[_colLicense.Index].Value = row.LicenseKey;
                }

                if (!row.HasOptions)
                {
                    // UseColumnTextForButtonValue=true (Spaltenebene) wuerde den
                    // Button-Text sonst fuer ALLE Zellen erzwingen und ein
                    // leeres cell.Value ignorieren - deshalb hier zusaetzlich
                    // pro Zelle auf false setzen, um das Zahnrad fuer Zeilen
                    // ohne Optionen wirklich auszublenden.
                    var optionsCell = (DataGridViewButtonCell)gridRow.Cells[_colOptions.Index];
                    optionsCell.UseColumnTextForButtonValue = false;
                    optionsCell.Value = "";
                    optionsCell.ReadOnly = true;
                    optionsCell.Style.BackColor = SystemColors.Control;
                }
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == _colSelected.Index)
                UpdateInstallButtonEnabled();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != _colOptions.Index) return;
            if (!(_grid.Rows[e.RowIndex].DataBoundItem is AppRow row)) return;
            if (!row.HasOptions) return;

            using (var dlg = new OptionsDialog(row))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && row.LicenseKeyEditable)
                {
                    if (dlg.RememberLicenseInVault)
                        WindowsCredentialStore.Save(WindowsCredentialStore.KeyForApp(row.Source.id), row.LicenseKey);
                    else
                        WindowsCredentialStore.Delete(WindowsCredentialStore.KeyForApp(row.Source.id));

                    _grid.Rows[e.RowIndex].Cells[_colLicense.Index].Value = row.LicenseKey;
                }
            }
        }

        private void SetAllSelected(bool value)
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
                gridRow.Cells[_colSelected.Index].Value = value;
            UpdateInstallButtonEnabled();
        }

        private void UpdateInstallButtonEnabled()
        {
            _btnInstall.Enabled = !_busy && _rows != null && _rows.Any(r => r.Selected);
        }

        private void RefreshAllInstalledVersions()
        {
            foreach (var row in _rows)
                row.RefreshInstalledVersion();

            foreach (DataGridViewRow gridRow in _grid.Rows)
                if (gridRow.DataBoundItem is AppRow row)
                    gridRow.Cells[_colInstalled.Index].Value = row.InstalledVersion;

            _lblStatus.Text = "Installationsstatus aktualisiert.";
        }

        // ---------------------------------------------------------------
        // Installation
        // ---------------------------------------------------------------

        private async void BtnInstall_Click(object sender, EventArgs e)
        {
            var selected = _rows.Where(r => r.Selected).ToList();
            if (selected.Count == 0) return; // Button ist deaktiviert - doppelte Absicherung

            SetBusy(true);
            _txtLog.Clear();
            _progressBar.Value = 0;
            var done = 0;
            var failed = 0;
            var skipped = 0;

            foreach (var row in selected)
            {
                _lblStatus.Text = $"Bearbeite {row.Name} ({done + 1}/{selected.Count}) ...";
                AppendLog($"=== {row.Name} ===");

                if (_chkSkipInstalled.Checked && row.InstalledVersion != "–")
                {
                    AppendLog($"Übersprungen: bereits installiert (Version {row.InstalledVersion}). " +
                              "Häkchen oben entfernen, um trotzdem neu zu installieren/zu aktualisieren.");
                    skipped++;
                }
                else
                {
                    try
                    {
                        if (!await InstallOneAsync(row))
                            failed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        AppendLog("FEHLER: " + ex.Message);
                    }

                    row.RefreshInstalledVersion();
                    RefreshInstalledCell(row);
                }

                done++;
                _progressBar.Value = Math.Min(100, (int)(done * 100.0 / selected.Count));
            }

            var ok = done - failed - skipped;
            _lblStatus.Text = $"Fertig: {ok} erfolgreich, {skipped} übersprungen, {failed} fehlgeschlagen.";

            SaveUserConfig();
            SetBusy(false);
        }

        private async void BtnPreload_Click(object sender, EventArgs e)
        {
            var selected = _rows.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Bitte zuerst mindestens ein Programm auswählen.", "Basic Installer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            _txtLog.Clear();
            _progressBar.Value = 0;
            var done = 0;

            foreach (var row in selected)
            {
                _lblStatus.Text = $"Lade herunter {row.Name} ({done + 1}/{selected.Count}) ...";
                var url = LanguageResolution.ResolveInstallerUrl(row.Source, row.SelectedLanguage);
                if (string.IsNullOrWhiteSpace(url))
                {
                    AppendLog($"{row.Name}: übersprungen, keine installer_url im Katalog.");
                }
                else
                {
                    try
                    {
                        await Downloader.DownloadAsync(url, row.Source.id, null, CancellationToken.None);
                        AppendLog($"{row.Name}: im Cache abgelegt ({Downloader.CacheDirectory}).");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"{row.Name}: Download fehlgeschlagen - {ex.Message}");
                    }
                }

                done++;
                _progressBar.Value = Math.Min(100, (int)(done * 100.0 / selected.Count));
            }

            _lblStatus.Text = "Vorladen abgeschlossen - Installation kann jetzt auch offline erfolgen.";
            SetBusy(false);
        }

        private async Task<bool> InstallOneAsync(AppRow row)
        {
            var app = row.Source;
            var installerUrl = LanguageResolution.ResolveInstallerUrl(app, row.SelectedLanguage);

            if (string.IsNullOrWhiteSpace(installerUrl))
            {
                AppendLog("Übersprungen: keine installer_url im Katalog gepflegt (siehe Hinweis in catalog/apps.json).");
                return false;
            }

            AppendLog("Lade herunter: " + installerUrl);
            string installerPath;
            try
            {
                var lastReported = -20;
                var progress = new Progress<int>(p =>
                {
                    if (p - lastReported < 20) return;
                    lastReported = p;
                    AppendLog($"  ... {p}%");
                });
                installerPath = await Downloader.DownloadAsync(installerUrl, app.id, progress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppendLog("Download fehlgeschlagen: " + ex.Message);
                return false;
            }

            AppendLog("Installiere (silent) ...");
            var result = await Task.Run(() =>
                SilentInstaller.Run(app, installerPath, row.InstallPath, row.SelectedLanguage, row.LicenseKey, row.Options));

            if (result.Outcome == InstallOutcome.Success)
            {
                AppendLog("OK.");
                return true;
            }

            AppendLog("Fehlgeschlagen: " + result.Message);
            return false;
        }

        private void RefreshInstalledCell(AppRow row)
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (ReferenceEquals(gridRow.DataBoundItem, row))
                {
                    gridRow.Cells[_colInstalled.Index].Value = row.InstalledVersion;
                    break;
                }
            }
        }

        private void AppendLog(string line)
        {
            _txtLog.AppendText(line + Environment.NewLine);
            InstallLog.Write(line); // dauerhafte Kopie - die Textbox geht beim Schliessen verloren
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _grid.Enabled = !busy;
            _btnSelectAll.Enabled = !busy;
            _btnSelectNone.Enabled = !busy;
            _btnRefreshStatus.Enabled = !busy;
            _btnPreload.Enabled = !busy;
            _btnCheckUpdate.Enabled = !busy;
            _chkSkipInstalled.Enabled = !busy;
            _cmbProfiles.Enabled = !busy;
            _btnSaveProfile.Enabled = !busy;
            _btnDeleteProfile.Enabled = !busy && _cmbProfiles.SelectedIndex > 0;
            UpdateInstallButtonEnabled();
        }

        // ---------------------------------------------------------------
        // Auto-Update (der App selbst, ueber GitHub Releases)
        // ---------------------------------------------------------------

        private async void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            _btnCheckUpdate.Enabled = false;
            await OfferUpdateIfAvailableAsync(interactive: true);
            _btnCheckUpdate.Enabled = true;
        }

        /// <summary>
        /// interactive=false (Programmstart): still, zeigt nur bei gefundenem
        /// Update einen Dialog. interactive=true (Button): meldet auch
        /// "kein Netz" bzw. "bereits aktuell" zurueck, damit ein Klick nie
        /// wirkungslos im Nichts verschwindet.
        /// </summary>
        private async Task OfferUpdateIfAvailableAsync(bool interactive)
        {
            var localVersion = FormatAssemblyVersion();
            _lblStatus.Text = "Suche nach App-Updates ...";

            var release = await AutoUpdateService.CheckForUpdateAsync();

            if (release == null)
            {
                _lblStatus.Text = "Bereit.";
                if (interactive)
                    MessageBox.Show(this, "Es konnte nicht auf Updates geprüft werden (kein Netz oder GitHub nicht erreichbar).",
                        "Basic Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!AutoUpdateService.IsNewer(release.Version, localVersion))
            {
                _lblStatus.Text = "Bereit.";
                if (interactive)
                    MessageBox.Show(this, $"Du hast bereits die aktuelle Version ({localVersion}).",
                        "Basic Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _lblStatus.Text = "Bereit.";
            var answer = MessageBox.Show(this,
                $"Version {release.Version} ist verfügbar (installiert: {localVersion}).\n\n" +
                "Jetzt herunterladen und installieren? Das Programm beendet sich dafür kurz und startet automatisch neu.",
                "Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                _lblStatus.Text = "Lade Update herunter ...";
                await AutoUpdateService.StageUpdateAndLaunchInstallerAsync(release, AppDomain.CurrentDomain.BaseDirectory);
                SaveUserConfig();
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Update fehlgeschlagen: " + ex.Message, "Basic Installer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "Bereit.";
            }
        }

        private static string FormatAssemblyVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }

        // ---------------------------------------------------------------
        // Administrator-Hinweis
        // ---------------------------------------------------------------

        private void UpdateElevationBanner()
        {
            _lblElevationWarning.Visible = !Utils.IsRunningElevated();
        }
    }
}
