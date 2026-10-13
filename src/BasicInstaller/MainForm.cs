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
    /// Pfad, ⚙-Optionen/Lizenz-Popup) + Installieren-Button + Protokoll.
    /// Sprache ist global (eine Auswahl fuer alle Apps, siehe
    /// _cmbGlobalLanguage/ApplyGlobalLanguage), Lizenz/Installationsoptionen
    /// leben bewusst NICHT als eigene Spalten, sondern nur im ⚙-Popup
    /// (OptionsDialog) - beides auf expliziten Nutzerwunsch nach einem ersten
    /// Praxistest so vereinfacht. Kontaktdaten und App-Update laufen ueber
    /// kleine Buttons/Popups, damit das Hauptfenster clean bleibt (siehe
    /// README "UX").
    /// </summary>
    public class MainForm : Form
    {
        private BindingList<AppRow> _rows;

        private DataGridView _grid;
        private DataGridViewCheckBoxColumn _colSelected;
        private DataGridViewTextBoxColumn _colName;
        private DataGridViewTextBoxColumn _colCategory;
        private DataGridViewTextBoxColumn _colInstalled;
        private DataGridViewTextBoxColumn _colLatest;
        private DataGridViewTextBoxColumn _colPath;
        private DataGridViewButtonColumn _colOptions;

        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Button _btnRefreshStatus;
        private Button _btnPreload;
        private Button _btnCheckUpdate;
        private ComboBox _cmbProfiles;
        private Button _btnSaveProfile;
        private Button _btnDeleteProfile;
        private ComboBox _cmbGlobalLanguage;
        private Label _lblVersionStatus;
        private Label _lblInstalledSummary;
        private Label _lblUpdatesSummary;
        private TextBox _txtSearch;
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
            BackColor = Theme.PageBackground;
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
                ForeColor = Theme.AccentDark,
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

            // Status-Badges nach Patch-My-PC-Vorbild: auf einen Blick sehen,
            // wie viele Apps schon installiert sind und ob Updates anstehen.
            var statusPills = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            _lblInstalledSummary = MakeStatusPill();
            _lblUpdatesSummary = MakeStatusPill();
            statusPills.Controls.Add(_lblInstalledSummary);
            statusPills.Controls.Add(_lblUpdatesSummary);
            header.Controls.Add(statusPills);

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
            // Bewusst klar von den Katalog-Apps abgegrenzt benannt - "App-Updates"
            // war mehrfach missverstanden worden als "Updates der installierten
            // Programme" statt "Update von Basic Installer selbst".
            _btnCheckUpdate = new Button { Text = "🔄 Basic Installer selbst aktualisieren", AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
            _btnSelectAll.Click += (s, e) => SetAllSelected(true);
            _btnSelectNone.Click += (s, e) => SetAllSelected(false);
            _btnRefreshStatus.Click += async (s, e) =>
            {
                _btnRefreshStatus.Enabled = false;
                await RefreshAllInstalledVersionsAsync();
                _btnRefreshStatus.Enabled = true;
            };
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

            // Sprache GLOBAL statt pro Zeile (Nutzerwunsch) - eine einzige
            // Auswahl fuer alle Apps, die ueberhaupt eine Sprachvariante im
            // Katalog hinterlegt haben (siehe ApplyGlobalLanguage).
            profileBar.Controls.Add(new Label { Text = "Sprache:", AutoSize = true, Margin = new Padding(24, 6, 4, 0) });
            _cmbGlobalLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            _cmbGlobalLanguage.Items.AddRange(new object[] { "Deutsch", "Englisch" });
            _cmbGlobalLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_rows == null) return; // Items werden vor LoadCatalogAndConfig befuellt
                ApplyGlobalLanguage((string)_cmbGlobalLanguage.SelectedItem);
            };
            profileBar.Controls.Add(_cmbGlobalLanguage);

            // Suchfeld direkt ueber der Tabelle (wie bei Patch My PC) - filtert
            // nach Name/Kategorie, blendet nicht passende Zeilen einfach aus.
            profileBar.Controls.Add(new Label { Text = "🔍 Suchen:", AutoSize = true, Margin = new Padding(24, 6, 4, 0) });
            _txtSearch = new TextBox { Width = 180 };
            _txtSearch.TextChanged += (s, e) => ApplySearchFilter();
            profileBar.Controls.Add(_txtSearch);

            BuildGrid();

            // War vorher eine Checkbox "Bereits installierte Programme
            // überspringen" - auf Nutzer-Feedback entfernt ("warum sollte man
            // das optional auswählen können"): Skip ist jetzt fest, keine
            // Abwahl mehr nötig/möglich. Die Zeile zeigt statt der Checkbox nun
            // dauerhaft den Basic-Installer-Versionsstatus (vorher nur in einer
            // MessageBox sichtbar, wenn man aktiv auf den Button klickte).
            var optionsBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(12, 4, 12, 0),
            };
            _lblVersionStatus = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = $"Basic Installer: installiert v{FormatAssemblyVersion()}",
            };
            optionsBar.Controls.Add(_lblVersionStatus);

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
                Height = 32,
                Width = 150,
                Enabled = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Theme.OnAccent,
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.FlatAppearance.MouseOverBackColor = Theme.AccentDark;
            _btnInstall.FlatAppearance.MouseDownBackColor = Theme.AccentDark;
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
            _lnkAbout = new LinkLabel { Text = "ℹ Über / Kontakt", AutoSize = true, LinkColor = Theme.AccentDark, ActiveLinkColor = Theme.Accent };
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
            // Beide Pruefungen beim Start, nicht nacheinander (unabhaengige
            // HttpClient-Aufrufe, kein gemeinsamer Zustand) - sonst wuerde die
            // App-Versionspruefung die Katalog-Versionspruefung unnoetig
            // verzoegern. Auf Nutzerwunsch: vorher lief die "Verfuegbar"-Pruefung
            // nur manuell per "Status aktualisieren". Button waehrend der
            // Start-Pruefung gesperrt, damit kein doppelter Lauf angestossen
            // werden kann.
            Shown += async (s, e) =>
            {
                _btnRefreshStatus.Enabled = false;
                await Task.WhenAll(
                    OfferUpdateIfAvailableAsync(interactive: false),
                    RefreshAllInstalledVersionsAsync());
                _btnRefreshStatus.Enabled = true;
            };
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
                // EnableHeadersVisualStyles=false ist Pflicht, sonst ignoriert
                // Windows die ColumnHeadersDefaultCellStyle-Farben komplett und
                // rendert den systemeigenen (grauen) Header-Stil.
                EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Theme.Accent,
                    ForeColor = Theme.OnAccent,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    SelectionBackColor = Theme.Accent,
                    SelectionForeColor = Theme.OnAccent,
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                },
                ColumnHeadersHeight = 30,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(0xF7, 0xF7, 0xF7) },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    SelectionBackColor = Theme.AccentLight,
                    SelectionForeColor = Color.Black,
                },
                GridColor = Color.FromArgb(0xE3, 0xE3, 0xE3),
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
            _colLatest = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "LatestVersion",
                HeaderText = "Verfügbar",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 75,
                ToolTipText = "Wird beim Start und per 'Status aktualisieren' ermittelt - " +
                               "nur fuer Apps moeglich, deren Download-Link die Version in der finalen " +
                               "URL zeigt. '–' heisst 'nicht ermittelbar', nicht 'kein Update'.",
            };
            _colPath = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "InstallPath",
                HeaderText = "Installationspfad",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 170,
                ToolTipText = "Nur änderbar, wenn der Installer einen Pfad-Switch unterstützt (siehe Katalog).",
            };
            _colOptions = new DataGridViewButtonColumn
            {
                HeaderText = "",
                Text = "⚙",
                UseColumnTextForButtonValue = true,
                // BUG (gefunden im Praxistest): AutoSizeMode.ColumnHeader sizt die
                // Spalte auf die Breite des (hier leeren) Headers - Width=36 wurde
                // dadurch beim Layout ignoriert und die Spalte auf nahezu 0px
                // zusammengeschrumpft, das Zahnrad war unsichtbar. "None" laesst
                // die explizite Width tatsaechlich gelten.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 40,
                ToolTipText = "Optionen/Lizenz - nur sichtbar wenn fürs Programm verfügbar.",
            };

            // Sprache (global statt pro Zeile, siehe _cmbGlobalLanguage im Toolbar)
            // und Lizenzschlüssel (nur noch im ⚙-Popup, nicht mehr als eigene
            // Spalte - beides auf Nutzerwunsch entfernt) sind bewusst KEINE
            // Grid-Spalten mehr.
            _grid.Columns.AddRange(_colSelected, _colName, _colCategory, _colInstalled, _colLatest, _colPath, _colOptions);
            _grid.DataBindingComplete += Grid_DataBindingComplete;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CellClick += Grid_CellClick;
            _grid.CellPainting += Grid_CellPainting;
        }

        /// <summary>
        /// Zeilen ohne Optionen/Lizenz zeigten in der Zahnrad-Spalte trotz leerem
        /// Text weiterhin einen leeren, klickbar wirkenden Button-Rahmen (im
        /// Praxistest gesehen). Hier wird fuer solche Zellen nur der Hintergrund
        /// gezeichnet, kein Button.
        /// </summary>
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _colOptions.Index) return;
            if (!(_grid.Rows[e.RowIndex].DataBoundItem is AppRow row)) return;
            if (row.HasOptions || row.LicenseKeyEditable) return;

            e.PaintBackground(e.ClipBounds, false);
            e.Handled = true;
        }

        private string BuildFooterText()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"© {DateTime.Now.Year} AS Computertechnik  –  v{version.Major}.{version.Minor}.{version.Build}";
        }

        private static Label MakeStatusPill() => new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0),
        };

        /// <summary>
        /// Aktualisiert die beiden Status-Badges. "Installiert" ist immer
        /// korrekt (Registry-Scan laeuft synchron beim Laden/Start-Button);
        /// "Updates verfuegbar" ist nur aussagekraeftig, NACHDEM einmal
        /// "Status aktualisieren" gelaufen ist (LatestVersion wird nicht
        /// automatisch beim Start geprueft) - zeigt bis dahin einen neutralen
        /// Hinweis statt "0 Updates" vorzutaeuschen.
        /// </summary>
        private void UpdateStatusPills()
        {
            if (_rows == null) return;

            var installedCount = _rows.Count(r => r.InstalledVersion != "–");
            _lblInstalledSummary.Text = $"✅ {installedCount}/{_rows.Count} installiert";
            _lblInstalledSummary.BackColor = Theme.SuccessLight;
            _lblInstalledSummary.ForeColor = Theme.Success;

            var checkedCount = _rows.Count(r => r.LatestVersion != "–");
            var updateCount = _rows.Count(r =>
                r.InstalledVersion != "–" && r.LatestVersion != "–" && r.InstalledVersion != r.LatestVersion);

            if (checkedCount == 0)
            {
                _lblUpdatesSummary.Text = "ℹ Updates noch nicht geprüft";
                _lblUpdatesSummary.BackColor = SystemColors.Control;
                _lblUpdatesSummary.ForeColor = Color.DimGray;
            }
            else if (updateCount > 0)
            {
                _lblUpdatesSummary.Text = $"⚠ {updateCount} Update{(updateCount == 1 ? "" : "s")} verfügbar";
                _lblUpdatesSummary.BackColor = Theme.AccentLight;
                _lblUpdatesSummary.ForeColor = Theme.AccentDark;
            }
            else
            {
                _lblUpdatesSummary.Text = "✅ Alles aktuell";
                _lblUpdatesSummary.BackColor = Theme.SuccessLight;
                _lblUpdatesSummary.ForeColor = Theme.Success;
            }
        }

        private void ApplySearchFilter()
        {
            var term = _txtSearch.Text.Trim();
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!(gridRow.DataBoundItem is AppRow row)) continue;
                gridRow.Visible = string.IsNullOrEmpty(term)
                    || row.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || row.Category.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
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
                    if (saved.options != null)
                        foreach (var kv in saved.options)
                            row.Options[kv.Key] = kv.Value;
                    if (saved.choice_options != null)
                        foreach (var kv in saved.choice_options)
                            row.ChoiceOptions[kv.Key] = kv.Value;
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

            // Sprache ist jetzt global (siehe ConfigStore.UserConfig.global_language) -
            // ComboBox-Auswahl setzt ApplyGlobalLanguage ueber ihr eigenes
            // SelectedIndexChanged bereits in Gang.
            var globalLanguage = string.IsNullOrWhiteSpace(savedConfig.global_language) ? "Deutsch" : savedConfig.global_language;
            _cmbGlobalLanguage.SelectedItem = globalLanguage;
            if (_cmbGlobalLanguage.SelectedItem == null) // falls ein alter/unbekannter Wert in config.json stand
                _cmbGlobalLanguage.SelectedIndex = 0;

            // "Standard"-Profil (falls vorhanden) laedt sich automatisch und
            // VOR dem zuletzt gespeicherten Zustand - Nutzerwunsch: "da erstellt
            // man Standard und das wird dann immer als erstes automatisch
            // geladen". Namensvergleich bewusst case-insensitive.
            var standardProfile = ProfileStore.LoadAll()
                .FirstOrDefault(kv => string.Equals(kv.Key, "Standard", StringComparison.OrdinalIgnoreCase));
            if (standardProfile.Value != null)
                ApplyUserConfig(standardProfile.Value);

            UpdateInstallButtonEnabled();
            UpdateStatusPills();
        }

        private void SaveUserConfig() => ConfigStore.Save(BuildCurrentUserConfig());

        /// <summary>
        /// Momentaufnahme der aktuellen Auswahl/Pfade/Optionen plus der globalen
        /// Spracheinstellung - Basis sowohl fuer config.json (automatisch bei
        /// jedem Beenden) als auch fuer benannte Profile (explizit per
        /// "Speichern als..."). Enthaelt bewusst NIE Lizenzschluessel/-dateien,
        /// weil AppUserSettings dafuer kein Feld hat - gilt dadurch automatisch
        /// auch fuer Profile.
        /// </summary>
        private UserConfig BuildCurrentUserConfig()
        {
            var cfg = new UserConfig
            {
                global_language = _cmbGlobalLanguage.SelectedItem as string ?? "Deutsch",
            };
            foreach (var row in _rows)
            {
                cfg.apps[row.Source.id] = new AppUserSettings
                {
                    selected = row.Selected,
                    install_path = row.PathEditable ? row.InstallPath : null,
                    options = row.Options.Count > 0 ? new Dictionary<string, bool>(row.Options) : null,
                    choice_options = row.ChoiceOptions.Count > 0 ? new Dictionary<string, string>(row.ChoiceOptions) : null,
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
                    if (saved.options != null)
                        foreach (var kv in saved.options)
                            row.Options[kv.Key] = kv.Value;
                    if (saved.choice_options != null)
                        foreach (var kv in saved.choice_options)
                            row.ChoiceOptions[kv.Key] = kv.Value;
                }
                else
                {
                    row.Selected = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(cfg.global_language) && _cmbGlobalLanguage.Items.Contains(cfg.global_language))
                _cmbGlobalLanguage.SelectedItem = cfg.global_language; // triggert ApplyGlobalLanguage selbst
            else
                ApplyGlobalLanguage(_cmbGlobalLanguage.SelectedItem as string ?? "Deutsch");

            // Einfachster robuster Weg, die Grid-Zellen nach Aenderungen an den
            // gebundenen Objekten neu zu rendern (BindingList meldet reine
            // Property-Aenderungen sonst nicht automatisch ans DataGridView).
            _grid.DataSource = null;
            _grid.DataSource = _rows;
            UpdateInstallButtonEnabled();
            UpdateStatusPills();
        }

        /// <summary>
        /// Setzt SelectedLanguage auf allen sprachfaehigen Zeilen auf die beste
        /// Uebereinstimmung mit der global gewaehlten Sprache (z.B. "de" oder
        /// "en-US" fuer "Englisch", je nachdem was die App im Katalog anbietet).
        /// Keine Aenderung, wenn die App gar keine Sprachvariante hat.
        /// </summary>
        private void ApplyGlobalLanguage(string globalChoice)
        {
            if (_rows == null) return;
            var prefix = globalChoice == "Englisch" ? "en" : "de";
            foreach (var row in _rows)
            {
                if (!row.LanguageSelectable) continue;
                var match = row.AvailableLanguages.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    row.SelectedLanguage = match;
            }
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

                if (!row.PathEditable)
                {
                    gridRow.Cells[_colPath.Index].ReadOnly = true;
                    gridRow.Cells[_colPath.Index].Style.BackColor = SystemColors.Control;
                    gridRow.Cells[_colPath.Index].Value = "(kein Pfad-Override möglich)";
                }

                // Zahnrad nur ausblenden, wenn die Zeile WEDER Options-Checkboxen
                // NOCH einen Lizenzmechanismus hat - sonst waere z.B. ESET (nur
                // Lizenz, keine "options") faelschlich ohne Zahnrad.
                if (!row.HasOptions && !row.LicenseKeyEditable)
                {
                    // UseColumnTextForButtonValue=true (Spaltenebene) wuerde den
                    // Button-Text sonst fuer ALLE Zellen erzwingen und ein
                    // leeres cell.Value ignorieren - deshalb hier zusaetzlich
                    // pro Zelle auf false setzen, um das Zahnrad fuer Zeilen
                    // ohne Optionen/Lizenz wirklich auszublenden.
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
            if (!row.HasOptions && !row.LicenseKeyEditable) return;

            using (var dlg = new OptionsDialog(row))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && row.LicenseKeyEditable)
                {
                    if (dlg.RememberLicenseInVault)
                        WindowsCredentialStore.Save(WindowsCredentialStore.KeyForApp(row.Source.id), row.LicenseKey);
                    else
                        WindowsCredentialStore.Delete(WindowsCredentialStore.KeyForApp(row.Source.id));
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

        private async Task RefreshAllInstalledVersionsAsync()
        {
            foreach (var row in _rows)
                row.RefreshInstalledVersion(); // Registry-Scan ist synchron und schnell

            _lblStatus.Text = "Prüfe verfügbare Versionen (nur für Apps mit erkennbarer URL) ...";

            // Parallel statt sequenziell - bei 14 Katalog-Eintraegen waere ein
            // 8-Sekunden-Timeout pro App sonst im ungluecklichsten Fall >100s.
            var checks = _rows.Select(async row =>
            {
                var url = LanguageResolution.ResolveInstallerUrl(row.Source, row.SelectedLanguage);
                row.LatestVersion = await LatestVersionChecker.TryGetLatestVersionAsync(url) ?? "–";
            });
            await Task.WhenAll(checks);

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!(gridRow.DataBoundItem is AppRow row)) continue;

                gridRow.Cells[_colInstalled.Index].Value = row.InstalledVersion;
                gridRow.Cells[_colLatest.Index].Value = row.LatestVersion;

                // Auf Nutzerwunsch: eine App mit erkanntem Update waehlt sich
                // selbst aus, "Installieren" reicht dann direkt. Nur ANhaken,
                // nie automatisch ABwaehlen - eine bewusste manuelle Auswahl
                // (z.B. "Nur herunterladen" vorbereiten) soll dadurch nicht
                // wieder verworfen werden.
                if (row.HasKnownUpdate)
                {
                    row.Selected = true;
                    gridRow.Cells[_colSelected.Index].Value = true;
                }

                // Dezente Hervorhebung, wenn beide Werte bekannt sind UND
                // voneinander abweichen - genau das war die zweimal gestellte
                // Nutzerfrage ("ob aktuell oder ob Update moeglich").
                var highlight = row.HasKnownUpdate ? Theme.AccentDark : Color.Empty;
                var boldFont = row.HasKnownUpdate ? new Font(_grid.Font, FontStyle.Bold) : null;

                // Name-Spalte mit einfaerben, nicht nur Installiert/Verfuegbar -
                // nach Patch-My-PC-Vorbild sticht eine Zeile mit anstehendem
                // Update schon beim Ueberfliegen der Namensliste ins Auge.
                gridRow.Cells[_colName.Index].Style.ForeColor = highlight;
                gridRow.Cells[_colName.Index].Style.Font = boldFont;
                gridRow.Cells[_colInstalled.Index].Style.ForeColor = highlight;
                gridRow.Cells[_colLatest.Index].Style.ForeColor = highlight;
                gridRow.Cells[_colLatest.Index].Style.Font = boldFont;
            }

            UpdateInstallButtonEnabled(); // falls die Auto-Auswahl oben den Installieren-Button gerade erst aktiviert hat
            UpdateStatusPills();
            // BUG (im Praxistest per UI-Automation bestaetigt): die Zellwerte oben
            // wurden korrekt gesetzt (ValuePattern lieferte z.B. "157.0.1"), aber das
            // Grid zeigte weiter "-", bis es von aussen neu gezeichnet wurde. Das
            // Setzen von Cell.Value an gebundenen Spalten invalidiert die Zelle hier
            // nicht zuverlaessig - deshalb explizit neu zeichnen.
            _grid.Invalidate();
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

                if (row.InstalledVersion != "–" && !row.HasKnownUpdate)
                {
                    // Fest, nicht mehr abwaehlbar (Nutzer-Feedback: ohne
                    // Versionsvergleich bringt eine Checkbox hier nur
                    // Verwirrung) - ABER nur, wenn kein Update bekannt ist.
                    // row.HasKnownUpdate kommt aus der (automatischen oder per
                    // "Status aktualisieren" manuellen) Versionspruefung; ist
                    // sie noch nie gelaufen, gilt die bisherige vorsichtige
                    // Regel (installiert = ueberspringen), da wir "aktuell vs.
                    // veraltet" dann schlicht nicht wissen.
                    AppendLog($"Übersprungen: bereits installiert (Version {row.InstalledVersion}).");
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

            UpdateStatusPills();
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

            var isPortable = string.Equals(app.installer_type, "portable", StringComparison.OrdinalIgnoreCase);
            AppendLog(isPortable ? "Lege portable Anwendung ab ..." : "Installiere (silent) ...");
            var result = await Task.Run(() =>
                SilentInstaller.Run(app, installerPath, row.InstallPath, row.SelectedLanguage, row.LicenseKey, row.BuildOptionSubstitutions()));

            if (result.Outcome == InstallOutcome.Success)
            {
                // Bei portablen Apps steckt in result.Message der Zielpfad -
                // genau die Info, die hier sonst verschluckt wuerde.
                AppendLog(string.IsNullOrWhiteSpace(result.Message) ? "OK." : result.Message);
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
            _lblStatus.Text = "Suche nach Basic-Installer-Updates ...";

            var release = await AutoUpdateService.CheckForUpdateAsync();

            if (release == null)
            {
                _lblStatus.Text = "Bereit.";
                // Ohne erfolgreiche Pruefung bleibt der Status unveraendert
                // (zeigt weiterhin nur die installierte Version) - kein "unbekannt"
                // vorgaukeln, wenn wir schlicht nicht nachsehen konnten.
                if (interactive)
                {
                    // AutoUpdateService.LastCheckError nennt den tatsaechlichen Grund
                    // (Netzwerkfehler, GitHub-Rate-Limit, kaputtes JSON, ...) statt
                    // immer derselben pauschalen Meldung - siehe Kommentar dort.
                    var reason = string.IsNullOrWhiteSpace(AutoUpdateService.LastCheckError)
                        ? "unbekannter Grund"
                        : AutoUpdateService.LastCheckError;
                    MessageBox.Show(this, $"Es konnte nicht auf Updates geprüft werden:\n\n{reason}",
                        "Basic Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (!AutoUpdateService.IsNewer(release.Version, localVersion))
            {
                _lblStatus.Text = "Bereit.";
                _lblVersionStatus.Text = $"Basic Installer: installiert v{localVersion} (aktuell)";
                _lblVersionStatus.ForeColor = Color.DimGray;
                if (interactive)
                    MessageBox.Show(this, $"Du hast bereits die aktuelle Version ({localVersion}).",
                        "Basic Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Ab hier steht fest: ein Update ist da - Status bleibt auch nach
            // dem Dialog sichtbar (vorher stand das NUR in der MessageBox, war
            // danach wieder weg, falls man "Nein" klickte).
            _lblVersionStatus.Text = $"Basic Installer: installiert v{localVersion} · verfügbar: v{release.Version} ⚠";
            _lblVersionStatus.ForeColor = Theme.AccentDark;
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
