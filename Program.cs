using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace Win11GamingOptimizer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private LibreHardwareMonitor.Hardware.Computer? hardwareComputer;
    private bool hardwareOpenAttempted;
    private readonly object hardwareSync = new();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

    private static void ApplyDarkScrollBar(Control control)
    {
        void Apply()
        {
            if (!control.IsHandleCreated) return;
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch { }
        }
        control.HandleCreated += (_, _) => Apply();
        Apply();
    }
    private Color themeAccent = Color.FromArgb(70, 130, 255);
    private Color themeInactiveButton = Color.FromArgb(24, 34, 50);
    private Color themeHoverButton = Color.FromArgb(38, 53, 76);
    private Color themePressedButton = Color.FromArgb(70, 130, 255);

    private readonly TextBox log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 16, 24),
        ForeColor = Color.FromArgb(220, 230, 244), Font = new Font("Consolas", 10),
        BorderStyle = BorderStyle.None
    };
    private readonly string journalPath = Path.Combine(AppContext.BaseDirectory, "Отчёты", "Журнал_действий.log");

    public MainForm()
    {
        Text = "Win11 Gaming Optimizer";
        Width = 1240; Height = 820; MinimumSize = new Size(1160, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 17, 27);
        ForeColor = Color.FromArgb(232, 237, 245);
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 16), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(12, 17, 27) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label { Text = "WIN11 GAMING OPTIMIZER", Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(0, 0) };
        var subtitle = new Label { Text = "Настройка игр и диагностика  •  Windows 11 x64", ForeColor = Color.FromArgb(170, 185, 204), AutoSize = true, Location = new Point(2, 45) };
        header.Controls.Add(title); header.Controls.Add(subtitle);
        var authorLink = new LinkLabel { Text = "Автор: @cireincroinsea", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), LinkColor = Color.FromArgb(0, 220, 255), ActiveLinkColor = Color.FromArgb(100, 240, 255), VisitedLinkColor = Color.FromArgb(0, 220, 255), BackColor = Color.Transparent, Location = new Point(2 + TextRenderer.MeasureText(subtitle.Text, subtitle.Font).Width + 10, 45), LinkBehavior = LinkBehavior.HoverUnderline };
        authorLink.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo("https://t.me/cireincroinsea") { UseShellExecute = true }); } catch { } };
        header.Controls.Add(authorLink);
        subtitle.Text = "Настройка игр и диагностика  •  Windows 11 x64  •";
        void PositionAuthor() { authorLink.Location = new Point(subtitle.Left + TextRenderer.MeasureText(subtitle.Text, subtitle.Font).Width + 6, subtitle.Top); }
        PositionAuthor();
        header.Resize += (_, _) => PositionAuthor();
        var journalTopButton = new Button { Text = "☷ ЖУРНАЛ", Width = 142, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(Width - 20 - 142 - 20, 6), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White, UseVisualStyleBackColor = false };
        journalTopButton.FlatAppearance.BorderSize = 0;
        header.Controls.Add(journalTopButton);
        header.Resize += (_, _) => journalTopButton.Location = new Point(header.ClientSize.Width - journalTopButton.Width - 8, 6);
        root.Controls.Add(header, 0, 0);

        // Custom navigation instead of the native TabControl: no system-drawn white borders.
        var tabs = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 21, 33), ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tabs.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 96, WrapContents = true, AutoScroll = false, BackColor = Color.FromArgb(14, 21, 33), Padding = new Padding(2, 2, 2, 0), Margin = new Padding(0, 0, 0, 8) };
        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 20, 29), Padding = new Padding(0), Margin = new Padding(0) };
        tabs.Controls.Add(nav, 0, 0);
        tabs.Controls.Add(pageHost, 0, 1);
        root.Controls.Add(tabs, 0, 1);

        var statusTab = new Panel { Tag = "Состояние ПК" };
        var temperatureTab = new Panel { Tag = "Температуры" };
        var networkTab = new Panel { Tag = "Сеть" };
        var settingsTab = new Panel { Tag = "Настройки программы" };
        var cleanupTab = new Panel { Tag = "Очистка" };
        var optimizeTab = new Panel { Tag = "Оптимизация" };
        var startupTab = new Panel { Tag = "Автозагрузка" };
        var updatesTab = new Panel { Tag = "Обновления Windows" };
        var gamesTab = new Panel { Tag = "Игры" };
        var bypassTab = new Panel { Tag = "Обход" };
        var hardTab = new Panel { Tag = "HARD" };
        var monitorTab = new Panel { Tag = "Монитор и драйверы" };
        var journalTab = new Panel { Tag = "Журнал" };
        var appPages = new Panel[] { statusTab, temperatureTab, networkTab, cleanupTab, optimizeTab, startupTab, updatesTab, gamesTab, bypassTab, hardTab, monitorTab, settingsTab };
        journalTab.Dock = DockStyle.Fill;
        journalTab.BackColor = Color.FromArgb(16, 20, 29);
        pageHost.Controls.Add(journalTab);
        journalTab.Visible = false;

        journalTopButton.Click += (_, _) => { foreach (Control c in pageHost.Controls) c.Visible = false; journalTab.Visible = true; journalTab.BringToFront(); foreach (Control c in nav.Controls) if (c is Button b) { b.BackColor = Color.FromArgb(25, 33, 47); } };

        foreach (var page in appPages)
        {
            page.Dock = DockStyle.Fill;
            page.BackColor = Color.FromArgb(16, 20, 29);
            pageHost.Controls.Add(page);
            page.Visible = false;
        }

        void SelectPage(Panel selected)
        {
            foreach (var page in appPages) page.Visible = false;
            journalTab.Visible = false;
            selected.Visible = true;
            foreach (Control c in nav.Controls)
            {
                if (c is Button b)
                {
                    bool active = string.Equals(b.Tag as string, selected.Tag as string, StringComparison.Ordinal);
                    b.BackColor = active ? themeAccent : themeInactiveButton;
                    b.FlatAppearance.MouseOverBackColor = themeHoverButton;
                    b.FlatAppearance.MouseDownBackColor = themePressedButton;
                    b.FlatAppearance.BorderSize = 0;
                }
            }
            selected.BringToFront();
        }

        foreach (var page in appPages)
        {
            string pageTitle = page.Tag as string ?? "";
            var pageButton = new Button
            {
                Text = pageTitle,
                Tag = pageTitle,
                Width = pageTitle == "Состояние ПК" ? 135 : pageTitle == "Температуры" ? 125 : pageTitle == "Сеть" ? 90 : pageTitle == "Настройки программы" ? 178 : pageTitle == "Обновления Windows" ? 180 : pageTitle == "Автозагрузка" ? 150 : pageTitle == "Обход" ? 112 : pageTitle == "Журнал" ? 100 : pageTitle == "HARD" ? 100 : pageTitle == "Монитор и драйверы" ? 190 : 132,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                BackColor = themeInactiveButton,
                ForeColor = Color.FromArgb(232, 237, 245),
                Margin = new Padding(0, 0, 8, 7),
                UseVisualStyleBackColor = false
            };
            pageButton.FlatAppearance.BorderSize = 0;
            pageButton.FlatAppearance.MouseOverBackColor = themeHoverButton;
            pageButton.FlatAppearance.MouseDownBackColor = themePressedButton;
            pageButton.Cursor = Cursors.Hand;
            pageButton.Click += (_, _) => SelectPage(page);
            nav.Controls.Add(pageButton);
        }
        SelectPage(statusTab);

        // Application appearance settings
        var settingsPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsTab.Controls.Add(settingsPanel);
        var settingsHeading = new Label { Text = "ВНЕШНИЙ ВИД ПРОГРАММЫ", Font = new Font("Segoe UI", 13, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 0, 0, 16) };
        settingsPanel.Controls.Add(settingsHeading, 0, 0);
        settingsPanel.SetColumnSpan(settingsHeading, 2);
        settingsPanel.Controls.Add(new Label { Text = "Цветовая тема", AutoSize = true, ForeColor = Color.Gainsboro, Anchor = AnchorStyles.Left }, 0, 1);
        var themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        themeCombo.Items.AddRange(new object[] { "Тёмно-синяя", "Графитовая", "Красные акценты", "Изумрудная", "Фиолетовая", "Океан", "Светлая", "Прозрачная (88%)" });
        themeCombo.SelectedIndex = 0;
        settingsPanel.Controls.Add(themeCombo, 1, 1);
        settingsPanel.Controls.Add(new Label { Text = "Размер текста", AutoSize = true, ForeColor = Color.Gainsboro, Anchor = AnchorStyles.Left }, 0, 2);
        var fontSize = new NumericUpDown { Minimum = 9, Maximum = 14, Value = 10, Width = 80 };
        settingsPanel.Controls.Add(fontSize, 1, 2);
        var applyAppearance = new Button { Text = "Применить внешний вид", Width = 210, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White };
        applyAppearance.Click += (_, _) =>
        {
            Font = new Font("Segoe UI", (float)fontSize.Value);
            tabs.Font = new Font("Segoe UI", (float)fontSize.Value);
            nav.Font = new Font("Segoe UI", (float)fontSize.Value);
            Color bg, surface, accent, fg;
            switch (themeCombo.SelectedIndex)
            {
                case 1: bg = Color.FromArgb(27, 29, 35); surface = Color.FromArgb(43, 46, 54); accent = Color.FromArgb(95, 105, 120); fg = Color.FromArgb(238, 240, 244); break;
                case 2: bg = Color.FromArgb(31, 17, 22); surface = Color.FromArgb(54, 28, 35); accent = Color.FromArgb(190, 45, 55); fg = Color.FromArgb(255, 235, 238); break;
                case 3: bg = Color.FromArgb(13, 29, 27); surface = Color.FromArgb(24, 48, 44); accent = Color.FromArgb(25, 155, 112); fg = Color.FromArgb(225, 255, 244); break;
                case 4: bg = Color.FromArgb(26, 19, 39); surface = Color.FromArgb(44, 32, 63); accent = Color.FromArgb(133, 82, 210); fg = Color.FromArgb(245, 235, 255); break;
                case 5: bg = Color.FromArgb(10, 30, 42); surface = Color.FromArgb(19, 48, 64); accent = Color.FromArgb(0, 145, 180); fg = Color.FromArgb(225, 249, 255); break;
                case 6: bg = Color.FromArgb(239, 242, 247); surface = Color.FromArgb(224, 229, 237); accent = Color.FromArgb(45, 108, 223); fg = Color.FromArgb(28, 34, 45); break;
                case 7: bg = Color.FromArgb(16, 20, 29); surface = Color.FromArgb(25, 33, 47); accent = Color.FromArgb(45, 108, 223); fg = Color.FromArgb(245, 248, 255); break;
                default: bg = Color.FromArgb(16, 20, 29); surface = Color.FromArgb(25, 33, 47); accent = Color.FromArgb(45, 108, 223); fg = Color.FromArgb(232, 237, 245); break;
            }
            themeAccent = accent;
            themeInactiveButton = surface;
            themeHoverButton = ControlPaint.Light(surface, 0.12f);
            themePressedButton = accent;
            BackColor = bg;
            ForeColor = fg;
            // WinForms supports whole-window translucency, not independent alpha for every control.
            Opacity = themeCombo.SelectedIndex == 7 ? 0.88 : 1.0;
            if (themeCombo.SelectedIndex == 7) AcrylicBackdrop.Enable(this.Handle);
            else AcrylicBackdrop.Disable(this.Handle);
            foreach (Panel page in appPages)
            {
                page.BackColor = bg;
                page.ForeColor = fg;
            }
            foreach (Control c in ControlsRecursive(tabs))
            {
                c.Font = new Font("Segoe UI", (float)fontSize.Value, c.Font.Style);
                if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel) c.BackColor = bg;
                if (c is Label label) label.ForeColor = label.Text.Contains("МИКРОФРИЗЫ") || label.Text.Contains("ИНТЕРНЕТ") ? Color.FromArgb(255, 130, 140) : fg;
                if (c is Button b)
                {
                    b.ForeColor = Color.White;
                    b.BackColor = b.Text == "СБРОСИТЬ ВСЕ ИЗМЕНЕНИЯ" ? Color.FromArgb(190, 35, 48) : accent;
                }
                if (c is TextBox tb) { tb.BackColor = Color.FromArgb(12, 16, 24); tb.ForeColor = fg; }
            }
            foreach (Control c in ControlsRecursive(tabs))
                if (c is Panel panel && panel.Controls.Count > 0) panel.BackColor = surface;
            applyAppearance.BackColor = accent;
            foreach (Control c in ControlsRecursive(this))
                if (c.IsHandleCreated && (c is ScrollableControl || c is TextBoxBase || c is ListBox || c is ListView))
                    DarkScrollBar.Apply(c.Handle, themeCombo.SelectedIndex != 6);
            tabs.Invalidate();
            nav.Invalidate();
            Log($"Внешний вид обновлён: {themeCombo.SelectedItem}, размер текста {fontSize.Value}.");
        };
        settingsPanel.Controls.Add(applyAppearance, 1, 3);
        var settingsInfo = new Label { Text = "Здесь меняется только внешний вид оптимизатора. Системные твики находятся во вкладке «Оптимизация».", AutoSize = true, ForeColor = Color.FromArgb(170, 185, 204), Margin = new Padding(0, 14, 0, 0) };
        settingsPanel.Controls.Add(settingsInfo, 0, 4);
        settingsPanel.SetColumnSpan(settingsInfo, 2);
        settingsPanel.Controls.Add(new Label { Text = "Язык интерфейса", AutoSize = true, ForeColor = Color.Gainsboro, Anchor = AnchorStyles.Left }, 0, 5);
        var languageCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        languageCombo.Items.AddRange(new object[] { "Русский", "English" });
        var languageFile = Path.Combine(AppContext.BaseDirectory, "config", "language.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(languageFile)!);
        string currentLanguage = File.Exists(languageFile) && File.ReadAllText(languageFile).Trim() == "en" ? "en" : "ru";
        languageCombo.SelectedIndex = currentLanguage == "en" ? 1 : 0;
        settingsPanel.Controls.Add(languageCombo, 1, 5);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Настройки программы"]="Settings", ["Очистка"]="Cleanup", ["Оптимизация"]="Optimization", ["Автозагрузка"]="Startup", ["Обновления Windows"]="Windows Updates", ["Игры"]="Games", ["Обход"]="Bypass", ["Журнал"]="Journal", ["БЕЗОПАСНО"]="LOW RISK", ["СРЕДНИЙ РИСК"]="MEDIUM RISK", ["ВЫСОКИЙ РИСК"]="HIGH RISK", ["ВНЕШНИЙ ВИД ПРОГРАММЫ"]="APPLICATION APPEARANCE", ["Цветовая тема"]="Color theme", ["Размер текста"]="Text size", ["Применить внешний вид"]="Apply appearance", ["Язык интерфейса"]="Interface language", ["Тёмно-синяя"]="Dark blue", ["Графитовая"]="Graphite", ["Красные акценты"]="Red accents", ["Изумрудная"]="Emerald", ["Фиолетовая"]="Purple", ["Океан"]="Ocean", ["Светлая"]="Light", ["Прозрачная (88%)"]="Transparent (88%)", ["Здесь меняется только внешний вид оптимизатора. Системные твики находятся во вкладке «Оптимизация»."]="This changes only the optimizer's appearance. System tweaks are in the Optimization tab.", ["Настройка игр и диагностика  •  Windows 11 x64  •  Автор: cireincroinsea"]="Game tuning and diagnostics  •  Windows 11 x64  •  Author: cireincroinsea", ["Внешний вид обновлён"]="Appearance updated", ["ОПТИМИЗАЦИЯ HARD"]="HARD OPTIMIZATION", ["СБРОСИТЬ ОПТИМИЗАЦИЮ"]="RESET OPTIMIZATION", ["СБРОСИТЬ ВСЕ ИЗМЕНЕНИЯ"]="RESET ALL CHANGES", ["Очистить"]="Clean", ["Применить"]="Apply", ["Обновить"]="Refresh", ["Диагностика ПК"]="PC diagnostics", ["Запустить"]="Run", ["Выключить"]="Disable", ["Включить"]="Enable", ["Удалить"]="Delete", ["Открыть"]="Open", ["Проверить"]="Check", ["Сохранить"]="Save", ["Отмена"]="Cancel", ["Да"]="Yes", ["Нет"]="No", ["Сохранить журнал как…"]="Save journal as…", ["Очистить отображение"]="Clear display"
        };
        var reverseTranslations = translations.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
        void ApplyLanguage(string lang)
        {
            bool english = lang == "en";
            foreach (Control c in ControlsRecursive(this))
            {
                if (c.Tag is string originalTag && (translations.ContainsKey(originalTag) || reverseTranslations.ContainsKey(originalTag)))
                    c.Tag = english ? (translations.TryGetValue(originalTag, out var t) ? t : originalTag) : (reverseTranslations.TryGetValue(originalTag, out var r) ? r : originalTag);
                if (english && translations.TryGetValue(c.Text, out var translated)) c.Text = translated;
                else if (!english && reverseTranslations.TryGetValue(c.Text, out var russian)) c.Text = russian;
                if (c is ComboBox cb && cb != languageCombo)
                {
                    int selected = cb.SelectedIndex;
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        string item = cb.Items[i]?.ToString() ?? "";
                        if (english && translations.TryGetValue(item, out var ti)) cb.Items[i] = ti;
                        else if (!english && reverseTranslations.TryGetValue(item, out var ri)) cb.Items[i] = ri;
                    }
                    if (selected >= 0 && selected < cb.Items.Count) cb.SelectedIndex = selected;
                }
            }
            foreach (Panel pg in appPages)
            {
                string tag = pg.Tag?.ToString() ?? "";
                pg.Tag = english && translations.TryGetValue(tag, out var et) ? et : !english && reverseTranslations.TryGetValue(tag, out var rt) ? rt : tag;
            }
            foreach (Control c in nav.Controls)
                if (c is Button b && b.Tag is string tag)
                {
                    string translatedTag = english && translations.TryGetValue(tag, out var et) ? et : !english && reverseTranslations.TryGetValue(tag, out var rt) ? rt : tag;
                    b.Tag = translatedTag; b.Text = translatedTag;
                }
            Text = english ? "Win11 Gaming Optimizer" : "Win11 Gaming Optimizer";
            File.WriteAllText(languageFile, lang);
            Log(english ? "Interface language changed to English." : "Язык интерфейса изменён на русский.");
        }
        languageCombo.SelectedIndexChanged += (_, _) => ApplyLanguage(languageCombo.SelectedIndex == 1 ? "en" : "ru");

        // Persistent action journal, stored beside the executable under Reports.
        var journalLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2, BackColor = Color.FromArgb(16, 20, 29) };
        journalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        journalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var journalActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.FromArgb(16, 20, 29) };
        var saveJournalButton = new Button { Text = "Сохранить журнал как…", Width = 190, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        saveJournalButton.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { FileName = "Журнал_действий.log", Filter = "Log / Text (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*", InitialDirectory = Path.Combine(AppContext.BaseDirectory, "Отчёты") };
            if (dlg.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dlg.FileName, log.Text, Encoding.UTF8); Log("Журнал экспортирован: " + dlg.FileName); }
        };
        var clearJournalButton = new Button { Text = "Очистить отображение", Width = 180, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        clearJournalButton.Click += (_, _) => { log.Clear(); Log("Очищено только отображение журнала; файл истории сохранён."); };
        journalActions.Controls.Add(saveJournalButton); journalActions.Controls.Add(clearJournalButton);
        log.Dock = DockStyle.Fill;
        journalLayout.Controls.Add(journalActions, 0, 0);
        journalLayout.Controls.Add(log, 0, 1);
        journalTab.Controls.Add(journalLayout);
        LoadJournal();

        // Scrollable list helpers
        TableLayoutPanel CreateTweakList(Panel page)
        {
            var sc = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 20, 12, 12), Margin = new Padding(0) };
            page.Controls.Add(sc);
            sc.BackColor = Color.FromArgb(16, 20, 29);
            sc.HandleCreated += (_, _) => DarkScrollBar.Apply(sc.Handle, themeCombo.SelectedIndex != 6);
            var tbl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 0, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            sc.Controls.Add(tbl);
            return tbl;
        }

        var cleanupList = CreateTweakList(cleanupTab);
        AddTweakRow(cleanupList, "Кэш шейдеров DirectX / NVIDIA", "Удаляет временные кэши шейдеров DirectX и NVIDIA. Первые запуски игр после очистки могут подтормаживать, пока кэш создаётся заново.", ClearShaderCaches, null, true);
        AddTweakRow(cleanupList, "Временные файлы пользователя", "Удаляет файлы из пользовательской папки TEMP, пропуская заблокированные файлы.", ClearTempFiles, null, true);
        AddTweakRow(cleanupList, "Корзина Windows", "Очищает корзину выбранных доступных дисков. Проверь содержимое перед очисткой.", ClearRecycleBin, null, true);
        AddTweakRow(cleanupList, "Очистка DNS-кэша", "Сбрасывает локальные DNS-записи. Может помочь при проблемах с адресами, но не снижает пинг сам по себе.", FlushDnsCache, null, true);
        AddTweakRow(cleanupList, "Системная папка TEMP", "Удаляет доступные временные файлы Windows из системного TEMP. Заблокированные файлы пропускаются; запуск от администратора может помочь.", ClearSystemTempFiles, null, true);
        AddTweakRow(cleanupList, "Кэш миниатюр проводника", "Удаляет кэш эскизов изображений и видео. Проводник создаст его заново; FPS в играх не меняется.", ClearThumbnailCache, null, true);
        AddTweakRow(cleanupList, "Кэш доставки обновлений", "Очищает временный кэш оптимизации доставки через штатный инструмент Windows. Обновления не отключаются.", ClearDeliveryOptimizationCache, null, true);
        AddTweakRow(cleanupList, "Отчёты об ошибках Windows", "Удаляет локальные отчёты WER, если они доступны. Это освобождает место, но не исправляет причину сбоев.", ClearWerReports, null, true);
        AddTweakRow(cleanupList, "Мягкая очистка памяти", "Освобождает неиспользуемую память самого оптимизатора. Не очищает RAM игр и других приложений: принудительная очистка системного кэша часто ухудшает загрузки и микрофреймтайм, поэтому Windows управляет им самостоятельно.", CleanOptimizerMemory, null, true);

        var optimizeList = CreateTweakList(optimizeTab);
        AddTweakRow(optimizeList, "Power Throttling", "Отключает механизм энергосбережения для фоновых приложений. Может увеличить потребление и нагрев, но обычно не нужен для игры, которая уже находится на переднем плане. Включай только для проверки конкретных просадок.", () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, "Power Throttling OFF"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", "Power Throttling"));
        AddTweakRow(optimizeList, "HAGS", "HAGS меняет способ планирования GPU-задач в Windows. На одних системах помогает, на других ничего не меняет или ухудшает плавность. Нужна перезагрузка; сравни средний FPS и 1% low в одной сцене.", () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, "HAGS включён"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", "HAGS"));
        AddTweakRow(optimizeList, "Network Throttling (устаревший параметр)", "Старый параметр мультимедийного профиля реестра без подтверждённого выигрыша в пинге или FPS. Не включай без конкретной диагностики: он может влиять на мультимедийные задачи. Сброс удаляет переопределение.", () => SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), "Network Throttling OFF"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", "Network Throttling"));
        AddTweakRow(optimizeList, "Ultimate Performance", "Переключает Windows на план с меньшим энергосбережением. Может повысить нагрев и расход энергии; FPS не гарантирован.", ApplyUltimatePerformance, RestoreBalanced);
        AddTweakRow(optimizeList, "Microsoft Defender: снизить нагрузку сканирования", "Задаёт рекомендованный средний предел CPU для фоновых проверок Defender — это не жёсткий лимит. Не отключает защиту в реальном времени. Требуются права администратора; сброс задаёт штатное значение 50%.", () => ConfigureDefenderScanCpu(20), () => ConfigureDefenderScanCpu(50));
        AddTweakRow(optimizeList, "Ограничение телеметрии", "Устанавливает политику диагностических данных Windows. Не отключает всю диагностику; Home может игнорировать часть политик.", () => SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, "Ограничение диагностических данных"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "Телеметрия Windows"));
        AddTweakRow(optimizeList, "Профиль против микрофризов", "Включает Game Mode и отключает Power Throttling. Не меняет спорные параметры MMCSS; прирост не гарантирован, проверяй frametime в одинаковой сцене.", ApplyStutterProfile, ResetStutterProfile);
        AddTweakRow(optimizeList, "Анимации Windows", "Уменьшает анимации меню и окон для отзывчивости интерфейса; напрямую FPS в играх обычно не повышает.", ApplyVisualEffects, ResetVisualEffects);
        AddTweakRow(optimizeList, "Отключить прозрачность Windows", "Отключает прозрачные эффекты интерфейса для текущего пользователя. Может немного упростить отрисовку рабочего стола, но прирост FPS в игре обычно минимален или отсутствует.", () => SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, "Прозрачность Windows выключена"), () => DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", "Прозрачность Windows"));
        AddTweakRow(optimizeList, "Убрать задержку автозапуска приложений", "Убирает дополнительную задержку запуска программ после входа в Windows. Может ускорить появление приложений на рабочем столе, но не увеличивает FPS в игре; сброс удаляет параметр и возвращает поведение Windows по умолчанию.", () => SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, "Задержка автозапуска приложений отключена"), () => DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", "Задержка автозапуска приложений"));
        AddTweakRow(optimizeList, "Отключить советы и предложения Windows", "Скрывает часть приветственных подсказок и предложений для текущего пользователя. Это настройка удобства/приватности, а не FPS-твик; системные обновления и безопасность не отключаются.", () => SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0, "Советы Windows выключены"), () => DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", "Советы Windows"));
        AddTweakRow(optimizeList, "Отключить P2P-раздачу обновлений", "Запрещает раздачу обновлений Windows другим устройствам через интернет. Обновления Windows остаются включены.", () => SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, "P2P-раздача обновлений выключена"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", "P2P-раздача обновлений"));
        AddTweakRow(optimizeList, "Отключить фоновые приложения (текущий пользователь)", "Ограничивает разрешения фоновых приложений политикой для поддерживаемых приложений. Может влиять на уведомления; эффект на FPS не гарантирован.", () => SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, "Фоновые приложения ограничены"), () => DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", "Фоновые приложения"));
        AddTweakRow(optimizeList, "Игровой режим Windows", "Включает Game Mode для текущего пользователя. Это штатная настройка Windows; результат зависит от игры.", ApplyGameMode, ResetGameMode);
        AddTweakRow(optimizeList, "Отключить фоновую запись игр (Game DVR)", "Отключает фоновый захват клипов Windows для текущего пользователя. Может снизить фоновую нагрузку, но запись клипов через Xbox Game Bar перестанет работать.", ApplyGameDvr, ResetGameDvr);
        AddTweakRow(optimizeList, "Отключить запись звука Game DVR", "Отключает только захват звука для игровых клипов текущего пользователя. Не гарантирует прирост FPS; используй, если не записываешь клипы.", () => SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AudioCaptureEnabled", 0, "Захват звука Game DVR выключен"), () => DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AudioCaptureEnabled", "Захват звука Game DVR"));
        AddTweakRow(optimizeList, "Оптимизация дисков Windows — не отключать", "Не отключай службу дефрагментации/оптимизации дисков вслепую: Windows использует TRIM для SSD и обслуживание HDD. Отключение может ухудшить обслуживание накопителя; если нужно, меняй расписание штатно через «Оптимизация дисков».", () => MessageBox.Show("Откройте dfrgui.exe — штатную «Оптимизацию дисков». Для SSD рекомендуется оставить автоматическое обслуживание включённым.", "Обслуживание дисков", MessageBoxButtons.OK, MessageBoxIcon.Information), null);
        AddTweakRow(optimizeList, "ЖЁСТКО: запретить автозапуск SysMain", "Меняет только тип запуска службы на Disabled и не останавливает уже работающую службу в текущем сеансе; эффект проявится после перезагрузки. На SSD это обычно не даёт выигрыша, а возврат к Automatic может не совпасть с прежней OEM-настройкой.", () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4, "SysMain отключён"), () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2, "SysMain — автоматический запуск"));
        AddTweakRow(optimizeList, "ЖЁСТКО: запретить автозапуск Windows Search", "Меняет только тип запуска службы на Disabled и не останавливает уже работающую службу в текущем сеансе; эффект проявится после перезагрузки. Поиск в меню Пуск и Проводнике может стать медленнее. Сброс задаёт Automatic и может не совпасть с прежней настройкой.", () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WSearch", "Start", 4, "Windows Search отключён"), () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WSearch", "Start", 2, "Windows Search — автоматический запуск"));
        AddTweakRow(optimizeList, "NTFS: отключить обновление времени последнего доступа", "Низкоуровневый параметр файловой системы без ожидаемого выигрыша в играх. Может мешать программам аудита и резервного копирования; оставляй системное значение, если не понимаешь конкретную потребность.", () => SetDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1, "Обновление времени доступа NTFS отключено"), () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", "Время последнего доступа NTFS"));

        var startupPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        startupTab.Controls.Add(startupPanel);
        var startupHeading = new Label { Text = "ПРОГРАММЫ АВТОЗАПУСКА", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 24) };
        var startupDescription = new Label { Text = "Просмотри приложения, запускаемые вместе с Windows. Отключай только знакомые элементы: не трогай драйверы, защиту и компоненты устройств без уверенности.", AutoSize = false, Width = 700, Height = 54, ForeColor = Color.FromArgb(170, 185, 204), Location = new Point(24, 64) };
        var startupOpen = new Button { Text = "Открыть менеджер автозапуска", Width = 260, Height = 38, Location = new Point(24, 132), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White };
        startupOpen.Click += (_, _) => OpenStartupManager();
        startupPanel.Controls.Add(startupHeading);
        startupPanel.Controls.Add(startupDescription);
        startupPanel.Controls.Add(startupOpen);

        var gamesPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16), BackColor = Color.FromArgb(16, 20, 29) };
        // Apply the selected Windows scrollbar theme specifically to the Games tab.
        // HandleCreated is important because the native scrollbar handle may not exist
        // when the initial theme pass runs.
        gamesPanel.HandleCreated += (_, _) =>
        {
            try { DarkScrollBar.Apply(gamesPanel.Handle, themeCombo.SelectedIndex != 6); } catch { }
        };
        gamesPanel.VisibleChanged += (_, _) =>
        {
            if (gamesPanel.IsHandleCreated)
                try { DarkScrollBar.Apply(gamesPanel.Handle, themeCombo.SelectedIndex != 6); } catch { }
        };
        gamesTab.Controls.Add(gamesPanel);
        var gamesLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        gamesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gamesPanel.Controls.Add(gamesLayout);

        void AddGameAction(string title, string description, string buttonText, Action action, bool red = false)
        {
            var card = new Panel { Height = 88, Dock = DockStyle.Top, BackColor = Color.FromArgb(25, 33, 47), Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12) };
            card.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(57, 70, 91)); e.Graphics.DrawLine(pen, 8, card.Height - 1, card.Width - 8, card.Height - 1); };
            var titleLabel = new Label { Text = title, Location = new Point(12, 8), Width = 650, Height = 22, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White, AutoEllipsis = true };
            var descLabel = new Label { Text = description, Location = new Point(12, 32), Width = 760, Height = 44, ForeColor = Color.FromArgb(170, 185, 204), AutoEllipsis = true };
            var actionButton = new Button { Text = buttonText, Width = 150, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, BackColor = red ? Color.FromArgb(190, 45, 55) : Color.FromArgb(45, 108, 223), ForeColor = Color.White };
            actionButton.FlatAppearance.BorderSize = 0;
            actionButton.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            card.Controls.Add(titleLabel); card.Controls.Add(descLabel); card.Controls.Add(actionButton);
            card.Resize += (_, _) => { actionButton.Location = new Point(Math.Max(12, card.ClientSize.Width - actionButton.Width - 12), 26); descLabel.Width = Math.Max(100, card.ClientSize.Width - 190); titleLabel.Width = Math.Max(100, card.ClientSize.Width - 190); };
            actionButton.Location = new Point(720, 26);
            gamesLayout.RowCount++;
            gamesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            gamesLayout.Controls.Add(card, 0, gamesLayout.RowCount - 1);
        }

        var gamesHeading = new Label { Text = "ОПТИМИЗАЦИЯ И ИСПРАВЛЕНИЕ ОШИБОК ИГР", AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0, 0, 0, 12) };
        gamesLayout.RowCount++;
        gamesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        gamesLayout.Controls.Add(gamesHeading, 0, gamesLayout.RowCount - 1);
        AddGameAction("Windows 11 — видеокарта для каждой игры", "Для каждой игры можно выбрать «Высокую производительность» и дискретный GPU, если он доступен. На той же странице есть оптимизация оконных игр DirectX 10/11; после изменения перезапусти игру.", "Настройки графики", () => Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics") { UseShellExecute = true }));
        AddGameAction("Windows 11 — режим питания", "На ноутбуке включай «Максимальная производительность» только при питании от сети: режим может увеличить нагрев и расход батареи. Изменение выполняется штатно в Windows.", "Настройки питания", () => Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true }));
        AddGameAction("Dota 2 — параметры запуска для высокого FPS", "Копирует безопасный стартовый набор: -novid +fps_max 0. Убирает стартовый ролик и снимает лимит FPS движка. Безлимитный FPS может увеличить нагрев и сделать frametime менее ровным; для стабильности часто лучше ограничить FPS под монитор.", "Настроить запуск", ApplyDotaLaunchOptions);
        AddGameAction("Dota 2 — приоритет процесса", "Если игра уже запущена, выставляет приоритет AboveNormal. Это не повышает производительность автоматически; если появляются рывки или проблемы со звуком, верни обычный приоритет.", "Приоритет", () => SetGamePriority(new[] { "dota2" }, "Dota 2"));
        AddGameAction("CS2 — приоритет процесса", "Если CS2 запущена, устанавливает приоритет AboveNormal. Не меняет графику и файлы игры; результат зависит от нагрузки.", "Оптимизировать", () => SetGamePriority(new[] { "cs2" }, "Counter-Strike 2"));
        AddGameAction("Dota 2 — проверить файлы", "Открывает штатную проверку файлов игры в Steam. Steam должен быть установлен; подтвердите действие в клиенте.", "Проверить файлы", () => OpenSteamValidation("570"), true);
        AddGameAction("CS2 — проверить файлы", "Открывает штатную проверку файлов Counter-Strike 2 в Steam. Это помогает при повреждённых или отсутствующих файлах.", "Проверить файлы", () => OpenSteamValidation("730"), true);
        AddGameAction("Dota 2 — очистить кэш шейдеров", "Удаляет только локальный shadercache Dota 2. Игра пересоздаст его, поэтому первые матчи могут подтормаживать. Закрой игру перед очисткой.", "Очистить кэш", () => ClearGameShaderCache("570", "Dota 2"), true);
        AddGameAction("CS2 — очистить кэш шейдеров", "Удаляет только локальный shadercache CS2. Игра пересоздаст его; первые запуски могут быть менее плавными. Закрой игру перед очисткой.", "Очистить кэш", () => ClearGameShaderCache("730", "Counter-Strike 2"), true);

        // HARD profile: explicit warning, single apply button and single reset button.
        var hardPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(22), BackColor = Color.FromArgb(16, 20, 29) };
        hardTab.Controls.Add(hardPanel);
        var hardHeading = new Label { Text = "HARD — АГРЕССИВНЫЙ ПРОФИЛЬ", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.White, Location = new Point(22, 20) };
        var hardDescription = new Label { Text = "Применяет набор системных настроек для уменьшения фоновой активности Windows и сетевой нагрузки. Универсального способа гарантировать большой прирост FPS или более стабильный интернет нет — результат зависит от драйверов, игры и оборудования.", AutoSize = false, Width = 880, Height = 72, ForeColor = Color.FromArgb(185, 198, 216), Location = new Point(22, 62) };
        var hardApply = new Button { Text = "ОПТИМИЗАЦИЯ HARD", Width = 250, Height = 44, Location = new Point(22, 150), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(190, 45, 55), ForeColor = Color.White };
        hardApply.FlatAppearance.BorderSize = 0;
        var hardReset = new Button { Text = "СБРОСИТЬ ОПТИМИЗАЦИЮ", Width = 270, Height = 44, Location = new Point(286, 150), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White };
        hardReset.FlatAppearance.BorderSize = 0;
        var hardWarning = new Label { Text = "ВНИМАНИЕ: после применения могут появиться проблемы с сетью, поиском Windows, уведомлениями, фоновыми приложениями или отдельными программами. Некоторые изменения требуют перезагрузки. Перед применением закрой игры и приложения. Автоматическая резервная копия не создаётся. Если возникнут проблемы — нажми «Сбросить оптимизацию» и перезагрузи ПК.", AutoSize = false, Width = 880, Height = 104, Location = new Point(22, 220), BackColor = Color.FromArgb(75, 35, 35), ForeColor = Color.FromArgb(255, 220, 220), Padding = new Padding(12) };
        var hardStatus = new Label { Text = "Статус: профиль не применён.", AutoSize = true, Location = new Point(22, 340), ForeColor = Color.FromArgb(170, 185, 204) };
        hardApply.Click += (_, _) =>
        {
            if (MessageBox.Show("Применить HARD-профиль?\n\nБудут изменены настройки фоновых служб и сетевого стека. Прирост FPS и стабильности сети не гарантируется. Автоматическая резервная копия не создаётся.", "Подтверждение HARD", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                ApplyHardProfile();
                hardStatus.Text = "Статус: профиль применён. Для части изменений нужна перезагрузка.";
                MessageBox.Show("HARD-профиль применён. Проверь игры и сеть. Если появились проблемы, используй «Сбросить оптимизацию».", "HARD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Не все настройки удалось применить.\n" + ex.Message, "HARD — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        hardReset.Click += (_, _) =>
        {
            if (MessageBox.Show("Сбросить настройки HARD-профиля к стандартным значениям Windows? Может потребоваться перезагрузка.", "Сброс HARD", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                ResetHardProfile();
                hardStatus.Text = "Статус: сброс выполнен. Перезагрузи ПК.";
                MessageBox.Show("Сброс выполнен. Перезагрузи ПК и проверь сеть и игры.", "HARD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Не все настройки удалось сбросить.\n" + ex.Message, "HARD — ошибка сброса", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        hardPanel.Controls.Add(hardHeading);
        hardPanel.Controls.Add(hardDescription);
        hardPanel.Controls.Add(hardApply);
        hardPanel.Controls.Add(hardReset);
        hardPanel.Controls.Add(hardWarning);
        hardPanel.Controls.Add(hardStatus);
        var nvidiaNote = new Label { Text = "NVIDIA: драйвер не предоставляет поддерживаемого встроенного способа надёжно переключить глобальный режим управления питанием из этого приложения. Нажми кнопку, затем в панели NVIDIA открой «Регулировка настроек изображения с просмотром» → «Использовать мои предпочтения» → «Производительность». Это изменяет качество изображения и может повысить энергопотребление.", AutoSize = false, Width = 900, Height = 94, Location = new Point(22, 390), ForeColor = Color.FromArgb(220, 230, 244), BackColor = Color.FromArgb(25, 33, 47), Padding = new Padding(10) };
        var nvidiaButton = new Button { Text = "ОТКРЫТЬ ПАНЕЛЬ УПРАВЛЕНИЯ NVIDIA", Width = 330, Height = 40, Location = new Point(22, 494), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        nvidiaButton.FlatAppearance.BorderSize = 0;
        nvidiaButton.Click += (_, _) => OpenNvidiaControlPanel();
        hardPanel.Controls.Add(nvidiaNote); hardPanel.Controls.Add(nvidiaButton);

        // External bypass projects: open the original GitHub repositories, never download or execute scripts automatically.
        var bypassPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), BackColor = Color.FromArgb(16, 20, 29) };
        bypassTab.Controls.Add(bypassPanel);
        var bypassLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, GrowStyle = TableLayoutPanelGrowStyle.AddRows, BackColor = Color.FromArgb(16, 20, 29) };
        bypassLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bypassPanel.Controls.Add(bypassLayout);
        var bypassHeading = new Label { Text = "ОБХОД ОГРАНИЧЕНИЙ", AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0, 0, 0, 8) };
        bypassLayout.RowCount++;
        bypassLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        bypassLayout.Controls.Add(bypassHeading, 0, bypassLayout.RowCount - 1);
        var bypassNote = new Label { Text = "Открывает официальные репозитории GitHub. Загрузка, установка и запуск выполняются вручную; программа ничего не скачивает и не запускает сама.", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.FromArgb(170, 185, 204), Margin = new Padding(0, 0, 0, 16) };
        bypassLayout.RowCount++;
        bypassLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bypassLayout.Controls.Add(bypassNote, 0, bypassLayout.RowCount - 1);
        void AddBypassCard(string titleText, string descriptionText, string url)
        {
            var card = new Panel { Height = 104, Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 33, 47), Margin = new Padding(0, 0, 0, 10), Padding = new Padding(12) };
            card.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(57, 70, 91)); e.Graphics.DrawLine(pen, 8, card.Height - 1, card.Width - 8, card.Height - 1); };
            var titleLabel = new Label { Text = titleText, Location = new Point(12, 9), Width = 620, Height = 24, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White, AutoEllipsis = true };
            var descLabel = new Label { Text = descriptionText, Location = new Point(12, 36), Width = 650, Height = 52, ForeColor = Color.FromArgb(170, 185, 204), AutoEllipsis = true };
            var openButton = new Button { Text = "Открыть GitHub", Width = 150, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            openButton.FlatAppearance.BorderSize = 0;
            openButton.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show("Не удалось открыть ссылку: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            card.Controls.Add(titleLabel); card.Controls.Add(descLabel); card.Controls.Add(openButton);
            card.Resize += (_, _) => { openButton.Location = new Point(Math.Max(12, card.ClientSize.Width - openButton.Width - 12), 32); titleLabel.Width = Math.Max(100, card.ClientSize.Width - 190); descLabel.Width = Math.Max(100, card.ClientSize.Width - 190); };
            openButton.Location = new Point(720, 32);
            bypassLayout.RowCount++;
            bypassLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            bypassLayout.Controls.Add(card, 0, bypassLayout.RowCount - 1);
        }
        AddBypassCard("Discord / YouTube — zapret", "Проект Flowseal для обхода сетевых ограничений Discord и YouTube. Перед запуском прочитай README и предупреждения автора.", "https://github.com/Flowseal/zapret-discord-youtube");
        AddBypassCard("Telegram Desktop — tg-ws-proxy", "Отдельный проект Flowseal для решения проблем подключения Telegram Desktop. Инструкции и релизы находятся в репозитории.", "https://github.com/Flowseal/tg-ws-proxy");
        AddBypassCard("VPN / прокси — NekoBox", "Клиент для работы с поддерживаемыми прокси/VPN-конфигурациями. Скачивай только подходящий релиз из GitHub и используй доверенные конфигурации.", "https://github.com/qr243vbi/nekobox/releases");

        var updatesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24), AutoScroll = true };
        updatesTab.Controls.Add(updatesPanel);
        var updatesHeading = new Label { Text = "УПРАВЛЕНИЕ ОБНОВЛЕНИЯМИ WINDOWS", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 24) };
        var updatesDescription = new Label { Text = "Можно временно запретить автоматическую установку обновлений политикой Windows. Это не отключает службы обновления и не гарантирует, что Windows Update полностью остановится. Используй сброс, чтобы удалить политику и вернуть стандартное поведение.", AutoSize = false, Width = 820, Height = 72, ForeColor = Color.FromArgb(170, 185, 204), Location = new Point(24, 66) };
        var updatesDisable = new Button { Text = "Отключить автообновление", Width = 250, Height = 38, Location = new Point(24, 154), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(190, 45, 55), ForeColor = Color.White };
        updatesDisable.Click += (_, _) => { try { DisableAutoUpdates(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        var updatesReset = new Button { Text = "Сбросить настройку", Width = 200, Height = 38, Location = new Point(286, 154), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(57, 66, 82), ForeColor = Color.White };
        updatesReset.Click += (_, _) => { try { ResetAutoUpdates(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        updatesPanel.Controls.Add(updatesHeading);
        updatesPanel.Controls.Add(updatesDescription);
        updatesPanel.Controls.Add(updatesDisable);
        updatesPanel.Controls.Add(updatesReset);
        var updatesStatus = new Label { Text = "Статус обновлений ещё не проверен.", AutoSize = false, Width = 820, Height = 54, ForeColor = Color.FromArgb(170, 185, 204), Location = new Point(24, 210) };
        var updatesCheck = new Button { Text = "Проверить состояние", Width = 220, Height = 36, Location = new Point(24, 274), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White };
        updatesCheck.Click += (_, _) =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                object? value = key?.GetValue("NoAutoUpdate");
                string policy = Convert.ToString(value) == "1" ? "политика NoAutoUpdate=1 найдена" : "политика полного запрета автообновлений не найдена";
                string service = RunCommand("sc.exe", "query wuauserv");
                bool running = service.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                string serviceConfig = RunCommand("sc.exe", "qc wuauserv");
                bool disabled = serviceConfig.Contains("DISABLED", StringComparison.OrdinalIgnoreCase);
                updatesStatus.Text = $"Проверка: {policy}. Служба Windows Update сейчас {(running ? "запущена" : "остановлена (это само по себе не ошибка)" )}; тип запуска: {(disabled ? "DISABLED" : "не Disabled / проверь вывод qc в журнале")}.\nПолитика не гарантирует исчезновения всех уведомлений и обновлений; Windows может менять поведение в зависимости от редакции и политик организации.";
                Log("Проверено состояние Windows Update: " + policy + "; service running=" + running + "; disabled start=" + disabled + "; config=" + serviceConfig.Replace(Environment.NewLine, " ").Trim());
            }
            catch (Exception ex) { updatesStatus.Text = "Не удалось проверить: " + ex.Message; Log("Ошибка проверки Windows Update: " + ex.Message); }
        };
        updatesPanel.Controls.Add(updatesStatus);
        updatesPanel.Controls.Add(updatesCheck);

        // Dashboard: live system overview; refresh automatically without requiring a button.
        var statusPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), BackColor = Color.FromArgb(16, 20, 29) };
        statusTab.Controls.Add(statusPanel);
        statusPanel.Controls.Add(new Label { Text = "СОСТОЯНИЕ ПК  /  ЖИВОЙ ОБЗОР СИСТЕМЫ", Font = new Font("Segoe UI", 17, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 20) });
        var statusInfo = new Label { Text = "Сбор сведений о системе…", AutoSize = false, Width = 1000, Height = 430, ForeColor = Color.FromArgb(220, 230, 244), Font = new Font("Segoe UI", 11), Location = new Point(24, 75) };
        statusPanel.Controls.Add(statusInfo);
        bool updatingStatus = false;
        async Task UpdateStatusAsync()
        {
            if (updatingStatus || !statusTab.Visible || IsDisposed) return;
            updatingStatus = true;
            try
            {
            string statusText = await Task.Run(() =>
            {
            var sbStatus = new StringBuilder();
            sbStatus.AppendLine("СИСТЕМА");
            sbStatus.AppendLine($"Имя ПК: {Environment.MachineName}    |    Пользователь: {Environment.UserName}");
            sbStatus.AppendLine($"Windows: {Environment.OSVersion.VersionString}    |    Архитектура ОС: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
            try
            {
                using var osInfo = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
                foreach (ManagementObject item in osInfo.Get())
                    sbStatus.AppendLine($"Редакция Windows: {item["Caption"] ?? "н/д"} | версия {item["Version"] ?? "н/д"} | сборка {item["BuildNumber"] ?? "н/д"} | {item["OSArchitecture"] ?? "н/д"}");
            }
            catch { }
            sbStatus.AppendLine($"Логических процессоров: {Environment.ProcessorCount}    |    Время работы Windows: {TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss")}");
            try
            {
                using var board = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                var b = board.Get().Cast<ManagementObject>().FirstOrDefault();
                if (b != null) sbStatus.AppendLine($"Материнская плата: {b["Manufacturer"]} {b["Product"]}");
            }
            catch { }
            try
            {
                using var video = new ManagementObjectSearcher("SELECT Name, DriverVersion, VideoProcessor FROM Win32_VideoController");
                foreach (ManagementObject g in video.Get())
                    sbStatus.AppendLine($"Видеокарта: {g["Name"]} | драйвер {g["DriverVersion"]} | {g["VideoProcessor"]}");
            }
            catch { }
            sbStatus.AppendLine();
            sbStatus.AppendLine("ПРОЦЕССОР");
            try
            {
                using var cpu = new ManagementObjectSearcher("SELECT Name, LoadPercentage, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject item in cpu.Get())
                    sbStatus.AppendLine($"{item["Name"]} | загрузка {item["LoadPercentage"] ?? "н/д"}% | ядер {item["NumberOfCores"] ?? "н/д"} | потоков {item["NumberOfLogicalProcessors"] ?? "н/д"} | макс. частота {item["MaxClockSpeed"] ?? "н/д"} МГц");
            }
            catch (Exception ex) { sbStatus.AppendLine("CPU: не удалось прочитать WMI (" + ex.Message + ")"); }
            sbStatus.AppendLine();
            sbStatus.AppendLine("ОПЕРАТИВНАЯ ПАМЯТЬ");
            try
            {
                using var os = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject item in os.Get())
                {
                    double total = Convert.ToDouble(item["TotalVisibleMemorySize"]) / 1024.0 / 1024.0;
                    double free = Convert.ToDouble(item["FreePhysicalMemory"]) / 1024.0 / 1024.0;
                    double used = Math.Max(0, total-free);
                    sbStatus.AppendLine($"Занято {used:F1} / {total:F1} ГБ ({(total > 0 ? used*100/total : 0):F0}%) | свободно {free:F1} ГБ");
                }
            }
            catch (Exception ex) { sbStatus.AppendLine("ОЗУ: не удалось прочитать (" + ex.Message + ")"); }
            sbStatus.AppendLine();
            sbStatus.AppendLine("ДИСКИ");
            try {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    double total = drive.TotalSize / 1e9, free = drive.AvailableFreeSpace / 1e9;
                    sbStatus.AppendLine($"{drive.Name} {drive.VolumeLabel} | свободно {free:F1} из {total:F1} ГБ | занято {(total-free):F1} ГБ ({(total > 0 ? (total-free)*100/total : 0):F0}%)");
                }
            } catch (Exception ex) { sbStatus.AppendLine("Диски: " + ex.Message); }
            sbStatus.AppendLine();
            sbStatus.AppendLine();
            sbStatus.AppendLine("Последнее обновление: " + DateTime.Now.ToString("HH:mm:ss"));
            return sbStatus.ToString();
            });
            if (!IsDisposed && !statusInfo.IsDisposed && statusTab.Visible)
                statusInfo.Text = statusText;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !statusInfo.IsDisposed) statusInfo.Text = "Не удалось обновить сведения: " + ex.Message;
            }
            finally { updatingStatus = false; }
        }
        var statusTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        statusTimer.Tick += async (_, _) => await UpdateStatusAsync();
        statusTab.VisibleChanged += (_, _) =>
        {
            if (statusTab.Visible) { _ = UpdateStatusAsync(); statusTimer.Start(); }
            else statusTimer.Stop();
        };
        statusTab.Disposed += (_, _) => { statusTimer.Stop(); statusTimer.Dispose(); };
        // Status is loaded when its page becomes visible; don't run expensive WMI queries during startup.

        var tempPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), BackColor = Color.FromArgb(16, 20, 29) };
        temperatureTab.Controls.Add(tempPanel);
        tempPanel.Controls.Add(new Label { Text = "ТЕМПЕРАТУРЫ КОМПОНЕНТОВ — АВТООБНОВЛЕНИЕ", Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 20) });
        var tempInfo = new RichTextBox { Text = "Подключение к датчикам…", ReadOnly = true, DetectUrls = false, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, Width = 1000, Height = 520, ForeColor = Color.FromArgb(220, 230, 244), BackColor = Color.FromArgb(5, 7, 11), Font = new Font("Consolas", 11), Location = new Point(24, 72), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        tempPanel.Controls.Add(tempInfo);
        ApplyDarkScrollBar(tempInfo);
        ApplyDarkScrollBar(tempPanel);
        tempPanel.Resize += (_, _) => { tempInfo.Width = Math.Max(400, tempPanel.ClientSize.Width - 72); tempInfo.Height = Math.Max(240, tempPanel.ClientSize.Height - 112); };

        // Keep the hardware provider open while the app is running. Re-opening it every tick
        // can repeatedly load/unload its sensor driver and create/delete files beside the EXE.
        bool updatingTemperatures = false;
        bool temperatureTabDisposed = false;
        int temperatureGeneration = 0;
        async Task UpdateTemperaturesAsync()
        {
            if (updatingTemperatures || !temperatureTab.Visible || IsDisposed) return;
            int generation = temperatureGeneration;
            updatingTemperatures = true;
            try
            {
                List<string> readings = await Task.Run(() =>
                {
                lock (hardwareSync)
                {
                var collectedReadings = new List<string>();
                if (!hardwareOpenAttempted)
                {
                    hardwareOpenAttempted = true;
                    hardwareComputer = new LibreHardwareMonitor.Hardware.Computer
                    {
                        IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
                        IsMemoryEnabled = true, IsStorageEnabled = true
                    };
                    hardwareComputer.Open();
                }
                if (hardwareComputer == null) throw new InvalidOperationException("Поставщик датчиков не создан.");
                void ReadHardware(LibreHardwareMonitor.Hardware.IHardware hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors.Where(x => x.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && x.Value.HasValue))
                        collectedReadings.Add($"{hardware.HardwareType,-12} | {hardware.Name} | {sensor.Name}: {sensor.Value!.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} °C");
                    foreach (var sub in hardware.SubHardware) ReadHardware(sub);
                }
                foreach (var hardware in hardwareComputer.Hardware) ReadHardware(hardware);
                return collectedReadings;
                }
                });
                if (temperatureTabDisposed || IsDisposed || !temperatureTab.Visible || generation != temperatureGeneration) return;
                var output = new StringBuilder();
                output.AppendLine("ТЕМПЕРАТУРЫ CPU / GPU / ПЛАТЫ / НАКОПИТЕЛЕЙ");
                output.AppendLine("────────────────────────────────────────────────────────────────────────────");
                var ramReadings = readings.Where(x => x.Contains("Memory", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("RAM", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("DIMM", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("DRAM", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("DDR", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("памят", StringComparison.OrdinalIgnoreCase)).ToList();
                if (ramReadings.Count > 0)
                {
                    
                    foreach (string reading in ramReadings) output.AppendLine(reading);
                }
                else
                {
                    output.AppendLine("Датчик температуры модулей памяти не обнаружен.");
                    output.AppendLine("Большинство модулей DDR4/DDR5 не предоставляет температуру без датчика на модуле.");
                }
                if (readings.Count == 0)
                    output.AppendLine("Датчики температуры не предоставлены драйвером/оборудованием.");
                else
                    foreach (string reading in readings.Where(x => !ramReadings.Contains(x))) output.AppendLine(reading);
                output.AppendLine();
                output.AppendLine("Обновлено: " + DateTime.Now.ToString("HH:mm:ss"));
                output.AppendLine("Показываются только реальные показания доступных датчиков.");
                tempInfo.SuspendLayout();
                int firstVisibleLine = Math.Max(0, tempInfo.GetLineFromCharIndex(tempInfo.GetCharIndexFromPosition(new Point(1, 1))));
                tempInfo.Text = output.ToString();
                int characterIndex = 0;
                foreach (string line in output.ToString().Split('\n'))
                {
                    string cleanLine = line.TrimEnd('\r');
                    Color lineColor = Color.FromArgb(220, 230, 244);
                    int marker = cleanLine.LastIndexOf("°C", StringComparison.OrdinalIgnoreCase);
                    if (marker >= 0)
                    {
                        int colon = cleanLine.LastIndexOf(':', marker);
                        if (colon >= 0 && double.TryParse(cleanLine.Substring(colon + 1, marker - colon - 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double celsius))
                        {
                            lineColor = celsius < 45 ? Color.FromArgb(75, 220, 145)
                                : celsius < 65 ? Color.FromArgb(255, 215, 75)
                                : celsius < 80 ? Color.FromArgb(255, 155, 65)
                                : Color.FromArgb(255, 85, 95);
                        }
                    }
                    tempInfo.Select(characterIndex, cleanLine.Length);
                    tempInfo.SelectionColor = lineColor;
                    characterIndex += cleanLine.Length + Environment.NewLine.Length;
                }
                int restoreIndex = tempInfo.GetFirstCharIndexFromLine(firstVisibleLine);
                if (restoreIndex >= 0) { tempInfo.Select(restoreIndex, 0); tempInfo.ScrollToCaret(); }
                tempInfo.SelectionLength = 0;
                tempInfo.SelectionColor = tempInfo.ForeColor;
                tempInfo.ResumeLayout();
            }
            catch (Exception ex)
            {
                tempInfo.Text = "Не удалось прочитать датчики:\n" + ex.Message + "\n\nПроверь драйверы и запуск от имени администратора.";
            }
            finally
            {
                updatingTemperatures = false;
                if (temperatureTabDisposed) { lock (hardwareSync) { try { hardwareComputer?.Close(); } catch { } } }
                else if (!IsDisposed && temperatureTab.Visible && generation != temperatureGeneration) _ = UpdateTemperaturesAsync();
            }
        }
        var tempTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        tempTimer.Tick += async (_, _) => await UpdateTemperaturesAsync();
        temperatureTab.VisibleChanged += (_, _) =>
        {
            if (temperatureTab.Visible) { _ = UpdateTemperaturesAsync(); tempTimer.Start(); }
            else { temperatureGeneration++; tempTimer.Stop(); }
        };
        temperatureTab.Disposed += (_, _) =>
        {
            tempTimer.Stop(); tempTimer.Dispose();
            temperatureTabDisposed = true;
            if (!updatingTemperatures) { lock (hardwareSync) { try { hardwareComputer?.Close(); } catch { } } }
        };
        // Don't open the hardware driver during startup if the temperature page isn't visible.
        // Network throughput, calculated from interface byte-counter deltas.
        var netPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), BackColor = Color.FromArgb(16, 20, 29) };
        networkTab.Controls.Add(netPanel);
        netPanel.Controls.Add(new Label { Text = "МОНИТОРИНГ СЕТИ", Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 20) });
        var netSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 560, Location = new Point(24, 72), BackColor = Color.FromArgb(25, 33, 47), ForeColor = Color.White };
        netPanel.Controls.Add(netSelect);
        var netRx = new Label { Text = "↓ ПОЛУЧЕНО   ожидание измерения", AutoSize = false, Width = 780, Height = 48, ForeColor = Color.FromArgb(75, 220, 145), Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(24, 125), BackColor = Color.FromArgb(18, 42, 35), Padding = new Padding(12, 8, 0, 0) };
        var netTx = new Label { Text = "↑ ОТДАНО       ожидание измерения", AutoSize = false, Width = 780, Height = 48, ForeColor = Color.FromArgb(90, 170, 255), Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(24, 181), BackColor = Color.FromArgb(20, 35, 55), Padding = new Padding(12, 8, 0, 0) };
        var netInfo = new Label { Text = "Скорость и объём трафика выбранного адаптера обновляются автоматически.", AutoSize = false, Width = 900, Height = 90, ForeColor = Color.FromArgb(220, 230, 244), Font = new Font("Segoe UI", 10), Location = new Point(24, 240) };
        netPanel.Controls.Add(netRx); netPanel.Controls.Add(netTx); netPanel.Controls.Add(netInfo);
        var processNetTitle = new Label { Text = "ТРАФИК ПО ПРОЦЕССАМ (СОРТИРОВКА ПО ОБЩЕМУ ОБЪЁМУ)", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold), Location = new Point(24, 345) };
        var processNetList = new ListBox { Location = new Point(24, 375), Width = 900, Height = 260, BackColor = Color.FromArgb(9, 12, 18), ForeColor = Color.FromArgb(220, 230, 244), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10), IntegralHeight = false };
        netPanel.Controls.Add(processNetTitle); netPanel.Controls.Add(processNetList);
        ApplyDarkScrollBar(processNetList);
        ApplyDarkScrollBar(netPanel);
        // Per-process traffic is collected from Windows kernel ETW TCP/IP events.
        // Requires elevation (the application manifest already requests administrator rights).
        var processNetTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        var processTraffic = new ConcurrentDictionary<int, (long Rx, long Tx)>();
        var processTrafficPrevious = new Dictionary<int, (long Rx, long Tx)>();
        TraceEventSession? activeNetworkTraceSession = null;
        Task? networkTraceTask = null;
        CancellationTokenSource? networkTraceCancellation = null;
        string? networkTraceError = null;
        long trafficSampleTicks = Stopwatch.GetTimestamp();

        void AddProcessTraffic(int pid, long bytes, bool received)
        {
            if (pid <= 0 || bytes <= 0) return;
            processTraffic.AddOrUpdate(pid,
                received ? (bytes, 0L) : (0L, bytes),
                (_, old) => received ? (old.Rx + bytes, old.Tx) : (old.Rx, old.Tx + bytes));
        }

        void StartProcessTraffic()
        {
            if (networkTraceTask is { IsCompleted: false }) return;
            networkTraceCancellation?.Dispose();
            networkTraceCancellation = new CancellationTokenSource();
            CancellationToken traceToken = networkTraceCancellation.Token;
            processTraffic.Clear();
            processTrafficPrevious.Clear();
            networkTraceError = null;
            string sessionName = "Win11GamingOptimizer_Network_" + Environment.ProcessId;
            networkTraceTask = Task.Run(() =>
            {
                try
                {
                    traceToken.ThrowIfCancellationRequested();
                    using var session = new TraceEventSession(sessionName);
                    session.StopOnDispose = true;
                    activeNetworkTraceSession = session;
                    // Kernel provider must be enabled before obtaining/using its parser,
                    // and can only be enabled once in this ETW session.
                    session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
                    session.Source.Kernel.TcpIpSend += data => AddProcessTraffic(data.ProcessID, data.size, false);
                    session.Source.Kernel.TcpIpRecv += data => AddProcessTraffic(data.ProcessID, data.size, true);
                    session.Source.Kernel.UdpIpSend += data => AddProcessTraffic(data.ProcessID, data.size, false);
                    session.Source.Kernel.UdpIpRecv += data => AddProcessTraffic(data.ProcessID, data.size, true);
                    if (traceToken.IsCancellationRequested) session.Stop();
                    session.Source.Process();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    networkTraceError = ex.Message;
                }
                finally { activeNetworkTraceSession = null; }
            });
            trafficSampleTicks = Stopwatch.GetTimestamp();
        }

        void StopProcessTraffic()
        {
            try { networkTraceCancellation?.Cancel(); } catch { }
            try { activeNetworkTraceSession?.Stop(); } catch { }
        }

        void RefreshProcessNetwork()
        {
            double elapsed = Math.Max(0.1, (Stopwatch.GetTimestamp() - trafficSampleTicks) / (double)Stopwatch.Frequency);
            trafficSampleTicks = Stopwatch.GetTimestamp();
            var snapshot = processTraffic.ToArray();
            var rows = new List<(int Pid, string Name, long Rx, long Tx, double RxRate, double TxRate)>();
            foreach (var pair in snapshot)
            {
                processTrafficPrevious.TryGetValue(pair.Key, out var prev);
                long rxDelta = Math.Max(0, pair.Value.Rx - prev.Rx);
                long txDelta = Math.Max(0, pair.Value.Tx - prev.Tx);
                string name;
                try { using var p = Process.GetProcessById(pair.Key); name = p.ProcessName; }
                catch
                {
                    // Kernel ETW reports a PID, not a process lifetime. Drop exited processes
                    // so their totals are not shown as live traffic or inherited by reused PIDs.
                    processTraffic.TryRemove(pair.Key, out _);
                    processTrafficPrevious.Remove(pair.Key);
                    continue;
                }
                rows.Add((pair.Key, name, pair.Value.Rx, pair.Value.Tx,
                    rxDelta / 1_000_000.0 / elapsed, txDelta / 1_000_000.0 / elapsed));
            }
            processTrafficPrevious.Clear();
            foreach (var pair in snapshot) processTrafficPrevious[pair.Key] = pair.Value;

            processNetList.BeginUpdate();
            processNetList.Items.Clear();
            foreach (var row in rows.OrderByDescending(x => x.Rx + x.Tx).ThenBy(x => x.Name))
                processNetList.Items.Add($"{row.Name} (PID {row.Pid}) — всего {(row.Rx + row.Tx) / 1_000_000.0:F2} MB | ↓ {row.Rx / 1_000_000.0:F2} MB ({row.RxRate:F3} MB/s) | ↑ {row.Tx / 1_000_000.0:F2} MB ({row.TxRate:F3} MB/s)");
            if (rows.Count == 0)
                processNetList.Items.Add(networkTraceError == null
                    ? "Ожидание сетевых событий ETW… откройте приложение/страницу для появления трафика."
                    : "Не удалось запустить учёт трафика: " + networkTraceError);
            processNetList.EndUpdate();
        }
        var netRefresh = new Button { Text = "Обновить список", Width = 170, Height = 36, Location = new Point(600, 70), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        netRefresh.FlatAppearance.BorderSize = 0; netPanel.Controls.Add(netRefresh);
        var netTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        long lastRx = 0, lastTx = 0; bool hasNetSample = false; var lastNetSampleAt = System.Diagnostics.Stopwatch.StartNew();
        int adapterRefreshTicks = 0;
        NetworkInterface[] currentNetAdapters = Array.Empty<NetworkInterface>();
        void RefreshNetAdapters()
        {
            string? previousName = (netSelect.SelectedIndex >= 0 && netSelect.SelectedIndex < currentNetAdapters.Length)
                ? currentNetAdapters[netSelect.SelectedIndex].Id : null;
            var refreshedAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            bool adaptersUnchanged = refreshedAdapters.Length == currentNetAdapters.Length
                && Enumerable.Range(0, refreshedAdapters.Length).All(i =>
                    refreshedAdapters[i].Id == currentNetAdapters[i].Id
                    && refreshedAdapters[i].Name == currentNetAdapters[i].Name
                    && refreshedAdapters[i].OperationalStatus == currentNetAdapters[i].OperationalStatus);
            if (adaptersUnchanged) return;
            currentNetAdapters = refreshedAdapters;
            netSelect.BeginUpdate();
            netSelect.Items.Clear();
            foreach (var a in currentNetAdapters)
                netSelect.Items.Add(a.Name + " [" + a.NetworkInterfaceType + "] — " + a.OperationalStatus);
            int selected = previousName == null ? -1 : Array.FindIndex(currentNetAdapters, a => a.Id == previousName);
            netSelect.SelectedIndex = selected >= 0 ? selected : (netSelect.Items.Count > 0 ? 0 : -1);
            netSelect.EndUpdate();
            hasNetSample = false;
            lastNetSampleAt.Restart();
        }
        void SampleNetwork()
        {
            if (netSelect.SelectedIndex < 0) { netInfo.Text = "Нет доступных сетевых адаптеров."; return; }
            if (netSelect.SelectedIndex >= currentNetAdapters.Length) { RefreshNetAdapters(); return; }
            var adapter = currentNetAdapters[netSelect.SelectedIndex];
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                hasNetSample = false;
                netRx.Text = "↓ ПОЛУЧЕНО   адаптер отключён";
                netTx.Text = "↑ ОТДАНО       адаптер отключён";
                netInfo.Text = $"Адаптер: {adapter.Name}{Environment.NewLine}Статус: {adapter.OperationalStatus}. Скорость недоступна.";
                return;
            }
            try
            {
                // IP statistics cover the selected interface across its IP traffic; keep the
                // selected adapter object stable so counters aren't accidentally taken from another NIC.
                var stats = adapter.GetIPStatistics();
                long rx = stats.BytesReceived, tx = stats.BytesSent;
                if (hasNetSample && (rx < lastRx || tx < lastTx)) hasNetSample = false;
                double elapsed = Math.Max(0.1, lastNetSampleAt.Elapsed.TotalSeconds);
                if (!hasNetSample)
                {
                    lastRx = rx; lastTx = tx; hasNetSample = true; lastNetSampleAt.Restart();
                    netRx.Text = "↓ ПОЛУЧЕНО   сбор первого интервала…";
                    netTx.Text = "↑ ОТДАНО       сбор первого интервала…";
                    netInfo.Text = $"Адаптер: {adapter.Name}{Environment.NewLine}Статус: {adapter.OperationalStatus}. Измеряется скорость за фактический интервал между снимками.";
                    return;
                }
                double down = Math.Max(0, (rx - lastRx) * 8.0 / 1_000_000.0 / elapsed);
                double up = Math.Max(0, (tx - lastTx) * 8.0 / 1_000_000.0 / elapsed);
                lastRx = rx; lastTx = tx; hasNetSample = true; lastNetSampleAt.Restart();
                netRx.Text = $"↓ ПОЛУЧЕНО   {down.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} Мбит/с  •  {(down / 8).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} МБ/с";
                netTx.Text = $"↑ ОТДАНО       {up.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} Мбит/с  •  {(up / 8).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} МБ/с";
                netInfo.Text = $"Адаптер: {adapter.Name}{Environment.NewLine}Статус: {adapter.OperationalStatus}  |  номинальная скорость адаптера: {(adapter.Speed > 0 ? (adapter.Speed / 1_000_000.0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " Мбит/с" : "н/д")}{Environment.NewLine}Всего получено: {rx / 1_000_000.0:F2} MB    •    Всего отправлено: {tx / 1_000_000.0:F2} MB (счётчики адаптера с момента сброса)";
            }
            catch (Exception ex) { netInfo.Text = "Не удалось прочитать счётчики: " + ex.Message; }
        }
        netRefresh.Click += (_, _) => RefreshNetAdapters();
        netSelect.SelectedIndexChanged += (_, _) => { hasNetSample = false; lastNetSampleAt.Restart(); };
        netTimer.Tick += (_, _) =>
        {
            try
            {
                if (++adapterRefreshTicks >= 5) { adapterRefreshTicks = 0; RefreshNetAdapters(); }
                SampleNetwork();
            }
            catch (Exception ex) { hasNetSample = false; netInfo.Text = "Ошибка сети: " + ex.Message; }
        };
        processNetTimer.Tick += (_, _) =>
        {
            if (networkTab.Visible && networkTraceError == null && (networkTraceTask == null || networkTraceTask.IsCompleted)) StartProcessTraffic();
            RefreshProcessNetwork();
        };
        networkTab.VisibleChanged += (_, _) =>
        {
            if (networkTab.Visible)
            {
                processNetList.BeginUpdate();
                processNetList.Items.Clear();
                processNetList.Items.Add("Подключение к ETW и сбор первых показаний…");
                processNetList.EndUpdate();
                StartProcessTraffic();
                netTimer.Start();
                processNetTimer.Start();
            }
            else { netTimer.Stop(); processNetTimer.Stop(); hasNetSample = false; StopProcessTraffic(); }
        };
        networkTab.Disposed += (_, _) =>
        {
            netTimer.Stop(); netTimer.Dispose(); processNetTimer.Stop(); processNetTimer.Dispose(); StopProcessTraffic();
            networkTraceCancellation?.Dispose(); networkTraceCancellation = null;
        };
        RefreshNetAdapters();
        // Sampling starts only while the network page is visible to avoid background UI work.

        // Live performance monitor and driver health checks.
        var monitorPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), BackColor = Color.FromArgb(16, 20, 29) };
        monitorTab.Controls.Add(monitorPanel);
        monitorPanel.HandleCreated += (_, _) => { try { DarkScrollBar.Apply(monitorPanel.Handle, true); } catch { } };
        ApplyDarkScrollBar(monitorPanel);
        var monitorHeading = new Label { Text = "ТОП ПРОЦЕССОВ ПО CPU И ПРОВЕРКА ДРАЙВЕРОВ", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 20) };
        var monitorInfo = new Label { Text = "Топ-15 процессов по CPU. Обновление каждые 5 секунд только при открытой вкладке.", AutoSize = false, Width = 850, Height = 42, ForeColor = Color.FromArgb(170, 185, 204), Location = new Point(24, 58) };
        var monitorValues = new Label { Text = "Ожидание открытия вкладки…", AutoSize = false, Width = 1000, Height = 430, ForeColor = Color.FromArgb(220, 230, 244), BackColor = Color.FromArgb(8, 10, 14), Font = new Font("Consolas", 10), Location = new Point(24, 108) };
        // Кнопки управления запуском/остановкой убраны: мониторинг включается автоматически
        // только пока вкладка видима.
        var driverCheck = new Button { Text = "Проверить драйверы", Width = 200, Height = 36, Location = new Point(24, 555), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 135, 88), ForeColor = Color.White };
        var driverHelp = new Button { Text = "Где проверить обновления", Width = 230, Height = 36, Location = new Point(236, 555), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        var bottleneckCheck = new Button { Text = "Полная диагностика узких мест", Width = 280, Height = 36, Location = new Point(24, 600), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 135, 88), ForeColor = Color.White };
        var deepSeekButton = new Button { Text = "Открыть DeepSeek", Width = 190, Height = 36, Location = new Point(316, 600), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        foreach (var b in new[] { driverCheck, driverHelp, bottleneckCheck, deepSeekButton }) { b.FlatAppearance.BorderSize = 0; monitorPanel.Controls.Add(b); }
        monitorPanel.Controls.Add(monitorHeading); monitorPanel.Controls.Add(monitorInfo); monitorPanel.Controls.Add(monitorValues);
        monitorPanel.SizeChanged += (_, _) => { try { if (monitorPanel.IsHandleCreated) DarkScrollBar.Apply(monitorPanel.Handle, true); monitorPanel.AutoScrollMinSize = new Size(0, 660); } catch { } };
        monitorPanel.AutoScrollMinSize = new Size(0, 660);
        var monitorTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        bool updatingPerformance = false;
        async Task UpdatePerformanceAsync()
        {
            if (updatingPerformance || !monitorTab.Visible || IsDisposed) return;
            updatingPerformance = true;
            try
            {
                string snapshot = await Task.Run(GetPerformanceSnapshot);
                if (!IsDisposed && !monitorValues.IsDisposed && monitorTab.Visible) monitorValues.Text = snapshot;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !monitorValues.IsDisposed) monitorValues.Text = "Не удалось обновить топ процессов CPU: " + ex.Message;
            }
            finally { updatingPerformance = false; }
        }
        monitorTimer.Tick += async (_, _) => await UpdatePerformanceAsync();
        monitorTab.VisibleChanged += (_, _) =>
        {
            if (monitorTab.Visible)
            {
                _ = UpdatePerformanceAsync();
                monitorTimer.Start();
            }
            else { monitorTimer.Stop(); }
        };
        monitorTab.Disposed += (_, _) => monitorTimer.Dispose();
        driverCheck.Click += async (_, _) => { driverCheck.Enabled = false; try { await CheckDriversAndDevicesAsync(); } catch (Exception ex) { Log("Ошибка проверки драйверов: " + ex.Message); MessageBox.Show(ex.Message, "Проверка драйверов", MessageBoxButtons.OK, MessageBoxIcon.Warning); } finally { if (!IsDisposed) driverCheck.Enabled = true; } };
        driverHelp.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); Log("Открыты дополнительные обновления Windows для проверки драйверов."); } catch { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); } };
        bottleneckCheck.Click += async (_, _) => { bottleneckCheck.Enabled = false; Log("Запущен анализ узких мест."); try { await DiagnoseAsync(); } catch (Exception ex) { Log("Ошибка анализа узких мест: " + ex.Message); MessageBox.Show(ex.Message, "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Warning); } finally { if (!IsDisposed) bottleneckCheck.Enabled = true; } };
        deepSeekButton.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://chat.deepseek.com/") { UseShellExecute = true }); Log("Открыт сайт DeepSeek Chat в браузере."); } catch (Exception ex) { Log("Не удалось открыть DeepSeek: " + ex.Message); MessageBox.Show("Не удалось открыть сайт DeepSeek. Открой вручную: https://chat.deepseek.com/\n" + ex.Message, "DeepSeek", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        root.Controls.Add(bottom, 0, 2);
        var diag = new Button { Text = "Диагностика ПК", Width = 180, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 108, 223), ForeColor = Color.White, Margin = new Padding(0, 0, 12, 0) };
        diag.FlatAppearance.BorderSize = 0;
        diag.Click += async (_, _) => { diag.Enabled = false; Log("Запущена диагностика ПК."); try { await DiagnoseAsync(); } catch (Exception ex) { Log("Ошибка диагностики: " + ex.Message); MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } finally { if (!IsDisposed) diag.Enabled = true; } };
        bottom.Controls.Add(diag);
        var reports = new Button { Text = "Папка отчётов", Width = 140, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White, Margin = new Padding(0, 0, 12, 0) };
        reports.FlatAppearance.BorderSize = 0; reports.Click += (_, _) => { Log("Открыта папка отчётов."); OpenReports(); }; bottom.Controls.Add(reports);
        var about = new Button { Text = "О программе", Width = 145, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 52, 73), ForeColor = Color.White };
        about.FlatAppearance.BorderSize = 0; about.Click += (_, _) => { Log("Открыто окно «О программе»."); About(); }; bottom.Controls.Add(about);
        var resetAll = new Button { Text = "СБРОСИТЬ ВСЕ ИЗМЕНЕНИЯ", Width = 225, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(190, 35, 48), ForeColor = Color.White, Margin = new Padding(14, 0, 0, 0) };
        resetAll.FlatAppearance.BorderSize = 0;
        resetAll.Click += (_, _) => { Log("Запрошен общий сброс изменений."); try { ResetAllChanges(); } catch (Exception ex) { Log("Ошибка общего сброса: " + ex.Message); MessageBox.Show(ex.Message, "Ошибка сброса", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        bottom.Controls.Add(resetAll);

        var footer = new Label { Text = "Твики применяются только по нажатию. Резервные копии автоматически не создаются. Защита Windows не отключается.", ForeColor = Color.FromArgb(170, 185, 204), AutoSize = true, Visible = false };

        if (currentLanguage == "en") ApplyLanguage("en");
        Log("Программа готова. Начни с диагностики.");
        Log("Универсальная версия: обнаруживает оборудование автоматически; не применяет опасные твики автоматически.");
    }

    private enum TweakRisk { Safe, Medium, High }

    private static TweakRisk GetTweakRisk(string name, string description, bool destructive)
    {
        string n = name.ToLowerInvariant();
        // High risk means destructive, likely to disrupt Windows features, or likely to cause a temporary regression.
        if (n.Contains("жёстко") || n.Contains("жестко") || n.Contains("sysmain") || n.Contains("index") ||
            n.Contains("индексирование") || n.Contains("ntfs") || n.Contains("shader") || n.Contains("шейдер") ||
            n.Contains("корзина") || n.Contains("системная папка temp") || n.Contains("ultimate performance"))
            return TweakRisk.High;
        if (destructive || n.Contains("hags") || n.Contains("network throttling") || n.Contains("power throttling") ||
            n.Contains("defender") || n.Contains("телеметр") || n.Contains("фоновые приложения") || n.Contains("задержку автозапуска") ||
            n.Contains("game dvr") || n.Contains("захват игр") || n.Contains("p2p") || n.Contains("последнего доступа"))
            return TweakRisk.Medium;
        if (n.Contains("игровой режим") || n.Contains("анимации windows") || n.Contains("прозрачность windows") ||
            n.Contains("советы и предложения") || n.Contains("временные файлы пользователя") ||
            n.Contains("кэш миниатюр") || n.Contains("отчёты об ошибках") || n.Contains("dns-кэш"))
            return TweakRisk.Safe;
        return TweakRisk.Medium;
    }

    private void AddTweakRow(TableLayoutPanel table, string name, string description, Action apply, Action? reset, bool destructive = false)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        bool gamingProfile = name.StartsWith("МИКРОФРИЗЫ") || name.StartsWith("ИНТЕРНЕТ");
        var risk = GetTweakRisk(name, description, destructive);
        Color riskColor = risk switch
        {
            TweakRisk.Safe => Color.FromArgb(36, 150, 92),
            TweakRisk.Medium => Color.FromArgb(190, 135, 25),
            _ => Color.FromArgb(190, 55, 62)
        };
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = gamingProfile ? Color.FromArgb(65, 27, 34) : Color.FromArgb(25, 33, 47), Margin = new Padding(0, 0, 0, 8), Padding = new Padding(10, 7, 8, 7) };
        panel.Paint += (_, e) =>
        {
            using var separator = new Pen(Color.FromArgb(57, 70, 91), 1);
            using var riskStripe = new SolidBrush(riskColor);
            e.Graphics.FillRectangle(riskStripe, 0, 0, 4, panel.Height);
            e.Graphics.DrawLine(separator, 8, panel.Height - 1, panel.Width - 8, panel.Height - 1);
        };
        var title = new Label { Text = name, AutoSize = false, Width = 320, Height = 24, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White, Location = new Point(10, 5), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true };
        var desc = new Label { Text = description, AutoSize = false, Width = 420, Height = 55, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(170, 185, 204), Location = new Point(10, 29), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var riskBadge = new Label
        {
            Text = risk switch { TweakRisk.Safe => "БЕЗОПАСНО", TweakRisk.Medium => "СРЕДНИЙ РИСК", _ => "ВЫСОКИЙ РИСК" },
            AutoSize = false, Width = 108, Height = 19, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = Color.White,
            BackColor = riskColor, Location = new Point(Math.Max(330, panel.ClientSize.Width - 120), 7),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        panel.Controls.Add(title); panel.Controls.Add(desc); panel.Controls.Add(riskBadge);
        panel.Resize += (_, _) =>
        {
            title.Width = Math.Max(180, panel.ClientSize.Width - riskBadge.Width - 34);
            desc.Width = Math.Max(180, panel.ClientSize.Width - 28);
            riskBadge.Left = Math.Max(title.Right + 8, panel.ClientSize.Width - riskBadge.Width - 10);
        };
        table.Controls.Add(panel, 0, row);
        var enable = new Button { Text = destructive ? "Очистить" : "Включить", Width = 82, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = gamingProfile ? Color.FromArgb(190, 45, 55) : Color.FromArgb(45, 108, 223), ForeColor = Color.White, Margin = new Padding(4, 0, 4, 0), Anchor = AnchorStyles.Right };
        enable.FlatAppearance.BorderSize = 0;
        bool isEnabled = !destructive && reset is not null && GetToggleState(name);
        void SetToggleVisual(bool enabled)
        {
            isEnabled = enabled;
            if (!destructive && reset is not null) SaveToggleState(name, enabled);
            enable.Text = enabled ? "Выключить" : (destructive ? "Очистить" : "Включить");
            enable.BackColor = enabled ? Color.FromArgb(75, 80, 89) : (gamingProfile ? Color.FromArgb(190, 45, 55) : Color.FromArgb(45, 108, 223));
            enable.ForeColor = Color.White;
            enable.FlatAppearance.MouseOverBackColor = enabled ? Color.FromArgb(90, 95, 104) : Color.FromArgb(55, 125, 238);
        }
        enable.Tag = (Action<bool>)SetToggleVisual;
        SetToggleVisual(isEnabled);
        enable.Click += (_, _) =>
        {
            try
            {
                if (!destructive && isEnabled)
                {
                    if (reset is null)
                    {
                        MessageBox.Show("Для этой настройки не задано действие выключения/сброса.", "Недоступно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Log("Запрошено выключение настройки: " + name);
                    reset();
                    SetToggleVisual(false);
                    Log("Настройка выключена/сброшена: " + name);
                }
                else
                {
                    Log("Запрошено применение настройки: " + name);
                    apply();
                    if (!destructive && reset is not null) SetToggleVisual(true);
                    Log("Обработана настройка: " + name);
                }
            }
            catch (OperationCanceledException) { Log("Операция отменена пользователем: " + name); }
            catch (Exception ex) { Log("Ошибка настройки «" + name + "»: " + ex.Message); MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        table.Controls.Add(enable, 1, row);
        var resetButton = new Button { Text = "Сбросить", Width = 82, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(57, 66, 82), ForeColor = Color.White, Margin = new Padding(4, 0, 4, 0), Anchor = AnchorStyles.Right, Enabled = reset is not null };
        resetButton.FlatAppearance.BorderSize = 0;
        if (reset is not null)
            resetButton.Click += (_, _) =>
            {
                try
                {
                    Log("Запрошен сброс настройки: " + name);
                    reset();
                    SetToggleVisual(false);
                    Log("Настройка сброшена: " + name);
                }
                catch (OperationCanceledException) { Log("Сброс отменён пользователем: " + name); }
                catch (Exception ex) { Log("Ошибка сброса «" + name + "»: " + ex.Message); MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
        table.Controls.Add(resetButton, 2, row);
    }

    private void AddButton(Control parent, string text, Action action, bool primary = false)
    {
        var b = new Button
        {
            Text = text, Width = 265, Height = 43, Margin = new Padding(0, 0, 0, 9),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(45, 108, 223) : Color.FromArgb(39, 52, 73),
            ForeColor = Color.White, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) =>
        {
            Log("Запущено действие: " + text);
            try { action(); Log("Действие завершено: " + text); }
            catch (OperationCanceledException) { Log("Действие отменено пользователем: " + text); }
            catch (Exception ex) { Log("Ошибка действия «" + text + "»: " + ex.Message); MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        parent.Controls.Add(b);
    }

    private void ApplyDotaLaunchOptions()
    {
        const string options = "-novid +fps_max 0";
        if (!Confirm("Будут скопированы параметры запуска Dota 2:\n\n" + options + "\n\nАвтоматически изменить Steam без подтверждения нельзя. После копирования откроется Steam. В свойствах Dota 2 вставь строку в поле «Параметры запуска».\n\nВажно: +fps_max 0 снимает лимит движка, но не гарантирует более плавную игру; при нестабильном frametime задай лимит под частоту монитора.")) return;
        Clipboard.SetText(options);
        Log("Dota 2: параметры запуска скопированы в буфер обмена: " + options);
        try { Process.Start(new ProcessStartInfo("steam://open/games") { UseShellExecute = true }); }
        catch (Exception ex) { Log("Dota 2: Steam не удалось открыть автоматически — " + ex.Message); }
        MessageBox.Show("Параметры скопированы:\n" + options + "\n\nВ Steam открой Библиотека → Dota 2 → Свойства → Параметры запуска и вставь строку.", "Dota 2 — параметры запуска", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenNvidiaControlPanel()
    {
        try
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe")
            };
            string? exe = candidates.FirstOrDefault(File.Exists);
            if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIAControlPanel") { UseShellExecute = true });
            Log("HARD: открыта панель управления NVIDIA. Режим «Производительность» нужно подтвердить вручную; автоматическое изменение профиля драйвера не заявляется.");
        }
        catch (Exception ex) { Log("NVIDIA Control Panel: ошибка открытия — " + ex.Message); MessageBox.Show("Не удалось открыть панель NVIDIA. Проверь, установлена ли она из Microsoft Store или вместе с драйвером.\n" + ex.Message, "NVIDIA", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void LoadJournal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
            if (File.Exists(journalPath))
            {
                // Keep the UI responsive when an old journal has grown very large.
                var lines = File.ReadLines(journalPath).TakeLast(5000);
                log.Text = string.Join(Environment.NewLine, lines);
                if (log.Text.Length > 0) log.AppendText(Environment.NewLine);
            }
        }
        catch { /* journaling must never prevent the optimizer from opening */ }
    }

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(message))); return; }
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        log.AppendText(line + Environment.NewLine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
            File.AppendAllText(journalPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            // Show a single useful warning in the UI, without recursively logging the file error.
            if (!log.Text.Contains("Не удалось записать журнал", StringComparison.Ordinal))
                log.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Не удалось записать журнал рядом с EXE: {ex.Message}{Environment.NewLine}");
        }
    }

    private static readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> processCpuSamples = new();

    private string? GetReportedGpuMemory()
    {
        lock (hardwareSync)
        {
            try
            {
                if (!hardwareOpenAttempted)
                {
                    hardwareOpenAttempted = true;
                    hardwareComputer = new LibreHardwareMonitor.Hardware.Computer
                    {
                        IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
                        IsMemoryEnabled = true, IsStorageEnabled = true
                    };
                    hardwareComputer.Open();
                }

                if (hardwareComputer == null) return null;
                foreach (var gpu in hardwareComputer.Hardware.Where(h =>
                    h.HardwareType is LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia
                        or LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                        or LibreHardwareMonitor.Hardware.HardwareType.GpuIntel))
                {
                    gpu.Update();
                    var totalMemory = gpu.Sensors.FirstOrDefault(s =>
                        s.Value.HasValue &&
                        (s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase)
                         || s.Name.Contains("Total GPU Memory", StringComparison.OrdinalIgnoreCase)
                         || s.Name.Contains("Total VRAM", StringComparison.OrdinalIgnoreCase)));
                    if (totalMemory?.Value is float value && float.IsFinite(value) && value > 0)
                        return $"{value:F1} GB (датчик LibreHardwareMonitor: {gpu.Name} / {totalMemory.Name})";
                }
            }
            catch { }
            return null;
        }
    }

    private static string GetPerformanceSnapshot()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ТОП-15 ПРОЦЕССОВ ПО ИСПОЛЬЗОВАНИЮ CPU");
        sb.AppendLine("Загрузка в % от общей мощности всех логических процессоров.");
        sb.AppendLine("В рейтинг попадают только процессы с достоверной разницей между двумя снимками.");
        var procSamples = new List<(string Name, int Id, double Cpu, long Ram)>();
        var seenProcessIds = new HashSet<int>();
        var now = DateTime.UtcNow;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var cpuTime = p.TotalProcessorTime;
                seenProcessIds.Add(p.Id);
                if (processCpuSamples.TryGetValue(p.Id, out var previous))
                {
                    double seconds = (now - previous.At).TotalSeconds;
                    TimeSpan cpuDelta = cpuTime - previous.Cpu;
                    if (seconds > 0 && cpuDelta >= TimeSpan.Zero)
                    {
                        double measuredCpu = cpuDelta.TotalMilliseconds / (seconds * Environment.ProcessorCount * 10.0);
                        if (double.IsFinite(measuredCpu) && measuredCpu >= 0 && measuredCpu <= 100)
                            procSamples.Add((p.ProcessName, p.Id, measuredCpu, p.WorkingSet64 / 1024 / 1024));
                    }
                }
                processCpuSamples[p.Id] = (cpuTime, now);
            }
            catch { }
            finally { p.Dispose(); }
        }
        foreach (int stalePid in processCpuSamples.Keys.Where(pid => !seenProcessIds.Contains(pid)).ToArray())
            processCpuSamples.Remove(stalePid);
        if (procSamples.Count == 0) sb.AppendLine("Недостаточно достоверных снимков; рейтинг появится после следующего обновления.");
        foreach (var x in procSamples.OrderByDescending(x => x.Cpu).Take(15))
            sb.AppendLine($"{x.Name,-28} PID {x.Id,-7} CPU {x.Cpu,5:F1}% | RAM {x.Ram:N0} МБ");
        sb.AppendLine();
        sb.AppendLine("Обновление каждые 5 секунд, только когда вкладка открыта.");
        return sb.ToString();
    }

    private async Task CheckDriversAndDevicesAsync()
    {
        string report = await Task.Run(() =>
        {
        var sb = new StringBuilder();
        sb.AppendLine("ПРОВЕРКА УСТРОЙСТВ И ДРАЙВЕРОВ");
        sb.AppendLine("Внимание: возраст драйвера сам по себе не означает, что его нужно обновлять. Сверяй модель и версию с производителем.");
        try
        {
            string problems = RunCommand("pnputil.exe", "/enum-devices /problem");
            sb.AppendLine("\nУстройства с сообщаемыми проблемами (pnputil):\n" + problems);
            if (problems.Contains("No devices", StringComparison.OrdinalIgnoreCase) || problems.Contains("Устройства не найдены", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("PnPUtil не сообщил устройства с проблемами.");
        }
        catch (Exception ex) { sb.AppendLine("PnPUtil: " + ex.Message); }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, InfName FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
            foreach (ManagementObject d in searcher.Get())
            {
                string name = Convert.ToString(d["DeviceName"]) ?? "";
                string version = Convert.ToString(d["DriverVersion"]) ?? "unknown";
                string dateRaw = Convert.ToString(d["DriverDate"]) ?? "";
                string dateText = "дата неизвестна";
                try { if (dateRaw.Length >= 8) dateText = ManagementDateTimeConverter.ToDateTime(dateRaw).ToString("yyyy-MM-dd"); } catch { }
                if (RegexLike(name, "NVIDIA|GeForce|AMD|Radeon|Intel.*Graphics|Ethernet|Wi-Fi|Wireless|Audio|Realtek|Bluetooth|Chipset|PCI"))
                    sb.AppendLine($"{name} | {d["Manufacturer"]} | версия {version} | дата {dateText} | {d["InfName"]}");
            }
        }
        catch (Exception ex) { sb.AppendLine("WMI driver inventory: " + ex.Message); }
        sb.AppendLine("\nКак искать обновления: Windows Update → Дополнительные параметры → Необязательные обновления → Обновления драйверов; для GPU используй NVIDIA App/официальный сайт NVIDIA, для AMD/Intel — их официальные утилиты. Не устанавливай драйвер только потому, что он старше нескольких лет.");
        return sb.ToString();
        });
        var dir = Path.Combine(AppContext.BaseDirectory, "Отчёты"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"Drivers_{DateTime.Now:yyyyMMdd_HHmmss}.txt"); File.WriteAllText(file, report, Encoding.UTF8);
        Log("Проверка драйверов завершена; отчёт: " + file);
        using var view = new ReportForm(report); view.ShowDialog(this);
    }

    private async Task DiagnoseAsync()
    {
        Log("Собираю сведения об оборудовании и сети...");
        string report = await Task.Run(() =>
        {
        var sb = new StringBuilder();
        var detectedIssues = new List<string>();
        sb.AppendLine("WIN11 GAMING OPTIMIZER — ДИАГНОСТИКА");
        sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Компьютер: {Environment.MachineName}");
        sb.AppendLine($"ОС: {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        try
        {
            using var osSearch = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject x in osSearch.Get())
            {
                sb.AppendLine($"Windows: {x["Caption"]} | version {x["Version"]} | build {x["BuildNumber"]}");
                try { var boot = ManagementDateTimeConverter.ToDateTime(Convert.ToString(x["LastBootUpTime"])); sb.AppendLine($"Последняя загрузка: {boot:yyyy-MM-dd HH:mm:ss} | uptime: {DateTime.Now - boot}"); } catch { }
            }
        }
        catch (Exception ex) { sb.AppendLine("Версия/uptime Windows: " + ex.Message); }
        try
        {
            using var boardSearch = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject x in boardSearch.Get()) sb.AppendLine($"Материнская плата: {x["Manufacturer"]} {x["Product"]}");
            using var biosSearch = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (ManagementObject x in biosSearch.Get()) sb.AppendLine($"BIOS: {x["Manufacturer"]} {x["SMBIOSBIOSVersion"]} | release {x["ReleaseDate"]}");
        }
        catch (Exception ex) { sb.AppendLine("Плата/BIOS: " + ex.Message); }
        sb.AppendLine();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            sb.AppendLine("=== CPU ===");
            foreach (ManagementObject x in searcher.Get())
                sb.AppendLine($"{x["Name"]} | cores: {x["NumberOfCores"]} | threads: {x["NumberOfLogicalProcessors"]}");
        }
        catch (Exception ex) { sb.AppendLine("CPU: " + ex.Message); }

        try
        {
            string? sensorMemory = GetReportedGpuMemory();
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            sb.AppendLine("\n=== GPU ===");
            if (sensorMemory != null) sb.AppendLine("Датчик общей памяти GPU: " + sensorMemory);
            foreach (ManagementObject x in searcher.Get())
            {
                object? adapterRam = x["AdapterRAM"];
                string memoryText = adapterRam is not null
                    ? $"WMI reports {Convert.ToDouble(adapterRam) / 1024 / 1024 / 1024:F1} GB (may be capped at 4 GB; check the LHM sensor above when available)"
                    : "VRAM capacity unavailable (WMI did not report it)";
                sb.AppendLine($"{x["Name"]} | driver: {x["DriverVersion"]} | VRAM: {memoryText}");
            }
        }
        catch (Exception ex) { sb.AppendLine("GPU: " + ex.Message); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject x in searcher.Get())
            {
                double total = Convert.ToDouble(x["TotalVisibleMemorySize"]) / 1024 / 1024;
                double free = Convert.ToDouble(x["FreePhysicalMemory"]) / 1024 / 1024;
                sb.AppendLine($"\nRAM: {total:F1} GB total, {free:F1} GB available");
            }
        }
        catch (Exception ex) { sb.AppendLine("RAM: " + ex.Message); }

        sb.AppendLine("\n=== POWER PLAN ===");
        sb.AppendLine(RunCommand("powercfg.exe", "/getactivescheme"));

        sb.AppendLine("\n=== PHYSICAL DISKS ===");
        try
        {
            using var diskSearch = new ManagementObjectSearcher("SELECT Model, InterfaceType, MediaType, Size, Status FROM Win32_DiskDrive");
            foreach (ManagementObject x in diskSearch.Get())
            {
                double size = x["Size"] is null ? 0 : Convert.ToDouble(x["Size"]) / 1e9;
                string diskStatus = Convert.ToString(x["Status"]) ?? "Unknown";
                sb.AppendLine($"{x["Model"]} | interface {x["InterfaceType"]} | media {x["MediaType"]} | {size:F0} GB | status {diskStatus}");
                if (!diskStatus.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    detectedIssues.Add($"Накопитель: WMI сообщает статус «{diskStatus}» для {x["Model"]}. Проверь SMART/диагностику производителя; WMI-статус не является полным SMART-тестом.");
            }
        }
        catch (Exception ex) { sb.AppendLine("Физические диски: " + ex.Message); }
        sb.AppendLine("\n=== ЛОГИЧЕСКИЕ ДИСКИ ===");
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            double freePct = 100.0 * d.AvailableFreeSpace / Math.Max(1, d.TotalSize);
            sb.AppendLine($"{d.Name} | format {d.DriveFormat} | free {d.AvailableFreeSpace / 1e9:F1} GB / total {d.TotalSize / 1e9:F1} GB | free {freePct:F1}%");
            if (freePct < 10 || d.AvailableFreeSpace < 15L * 1024 * 1024 * 1024)
                detectedIssues.Add($"Мало свободного места: диск {d.Name} имеет {freePct:F1}% свободного места ({d.AvailableFreeSpace / 1e9:F1} GB). Это может мешать обновлениям и создавать проблемы при больших загрузках.");
        }

        sb.AppendLine("\n=== NETWORK ADAPTERS (ALL) ===");
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            sb.AppendLine($"{n.Name} | {n.NetworkInterfaceType} | status {n.OperationalStatus} | {n.Speed / 1_000_000} Mbps | {n.Description}");
            try
            {
                var ip = n.GetIPProperties();
                foreach (var a in ip.UnicastAddresses) sb.AppendLine($"  IP: {a.Address}");
                foreach (var g in ip.GatewayAddresses) sb.AppendLine($"  Gateway: {g.Address}");
                foreach (var d in ip.DnsAddresses) sb.AppendLine($"  DNS: {d}");
            }
            catch { }
        }
        sb.AppendLine("\n=== ACTIVE CONNECTION COUNTERS ===");
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var st = n.GetIPv4Statistics();
                sb.AppendLine($"{n.Name} | received {st.BytesReceived / 1e6:F1} MB | sent {st.BytesSent / 1e6:F1} MB | errors rx/tx {st.IncomingPacketsWithErrors}/{st.OutgoingPacketsWithErrors} | discards rx/tx {st.IncomingPacketsDiscarded}/{st.OutgoingPacketsDiscarded}");
                if (st.IncomingPacketsWithErrors > 0 || st.OutgoingPacketsWithErrors > 0)
                    detectedIssues.Add($"Сеть: на адаптере «{n.Name}» есть накопленные ошибки пакетов (приём/отправка {st.IncomingPacketsWithErrors}/{st.OutgoingPacketsWithErrors}). Счётчики с момента запуска/сброса; сравни повторный отчёт, проверь кабель/порт/драйвер.");
            }
            catch { }
        }

        sb.AppendLine("\n=== PING TEST ===");
        try
        {
            using var ping = new Ping();
            var times = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                var reply = ping.Send("1.1.1.1", 1200);
                if (reply.Status == IPStatus.Success) { times.Add(reply.RoundtripTime); sb.AppendLine($"Attempt {i + 1}: {reply.RoundtripTime} ms"); }
                else sb.AppendLine($"Attempt {i + 1}: {reply.Status}");
            }
            if (times.Count > 0)
            {
                double avgPing = times.Average();
                long spread = times.Max() - times.Min();
                sb.AppendLine($"Min/avg/max: {times.Min()} / {avgPing:F1} / {times.Max()} ms");
                if (avgPing > 100)
                    detectedIssues.Add($"Высокий отклик до 1.1.1.1: среднее {avgPing:F1} мс. Это не измерение маршрута до игрового сервера; проверь ещё ping до адреса сервера.");
                if (spread > 35)
                    detectedIssues.Add($"Колебания отклика до 1.1.1.1: разница max–min {spread} мс на пяти пробах. Слишком мало измерений для диагноза; повтори тест в игре и без фоновых загрузок.");
            }
            else sb.AppendLine("Нет ответов. ICMP может блокироваться сетью.");
        }
        catch (Exception ex) { sb.AppendLine("Ping: " + ex.Message); }

        sb.AppendLine("\n=== RECENT SYSTEM EVENTS (display/WHEA/disk) ===");
        string eventOutput = RunCommand("wevtutil.exe", "qe System /q:\"*[System[(Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 604800000]]]\" /rd:true /c:150 /f:text");
        var eventLines = eventOutput.Split('\n').Where(line => RegexLike(line, "Display|nvlddmkm|WHEA|Disk|stornvme|storahci|4101|129|153")).Take(100).ToList();
        sb.AppendLine(string.Join(Environment.NewLine, eventLines));
        if (eventLines.Any(line => RegexLike(line, "Display|nvlddmkm|4101")))
            detectedIssues.Add("Графика: в журналах за 7 дней есть строки, похожие на события сброса/ошибок видеодрайвера. Проверь источник, ID события и точное время; фильтр по тексту даёт только предварительный сигнал.");
        if (eventLines.Any(line => RegexLike(line, "WHEA")))
            detectedIssues.Add("Аппаратные события: в журнале найдены строки WHEA за 7 дней. Нужны подробности события; само упоминание WHEA не определяет неисправную деталь.");
        if (eventLines.Any(line => RegexLike(line, "Disk|stornvme|storahci|\\b129\\b|\\b153\\b")))
            detectedIssues.Add("Накопитель/контроллер: в журнале найдены строки Disk/StorNVMe/StorAHCI или события 129/153. Проверь время события, кабель/порт для SATA и SMART/прошивку накопителя.");

        sb.AppendLine("\n=== GAME PROCESSES ===");
        foreach (string gameName in new[] { "cs2", "dota2", "steam", "Discord", "Telegram" })
        {
            var procs = Process.GetProcessesByName(gameName);
            sb.AppendLine($"{gameName}: {(procs.Length == 0 ? "не запущен" : "запущено " + procs.Length)}");
            foreach (var proc in procs)
            {
                try { sb.AppendLine($"  PID {proc.Id} | priority {proc.PriorityClass} | working set {proc.WorkingSet64 / 1e6:F0} MB"); } catch { }
                finally { proc.Dispose(); }
            }
        }
        sb.AppendLine("\n=== СОХРАНЁННОЕ СОСТОЯНИЕ КНОПОК (НЕ ФАКТИЧЕСКАЯ ПРОВЕРКА WINDOWS) ===");
        var toggleStates = LoadToggleStates();
        if (toggleStates.Count == 0) sb.AppendLine("Сохранённых состояний кнопок нет.");
        foreach (var kv in toggleStates.OrderBy(k => k.Key)) sb.AppendLine($"{(kv.Value ? "[ВКЛ]" : "[ВЫКЛ]")} {kv.Key}");
        try
        {
            if (File.Exists(ConfigPath))
            {
                var changes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigPath)) ?? new();
                sb.AppendLine($"Записей изменений в конфиге: {changes.Count}");
                foreach (var kv in changes.OrderBy(k => k.Key)) sb.AppendLine($"{kv.Key} = {kv.Value}");
            }
        }
        catch (Exception ex) { sb.AppendLine("Конфигурация изменений: " + ex.Message); }
        sb.AppendLine("\n=== QUICK CHECKS ===");
        sb.AppendLine("Windows Update service: " + RunCommand("sc.exe", "query wuauserv"));
        sb.AppendLine("DNS Client service: " + RunCommand("sc.exe", "query Dnscache"));
        sb.AppendLine("Firewall service: " + RunCommand("sc.exe", "query mpssvc"));
        sb.AppendLine("\n=== ОБНАРУЖЕННЫЕ ТИПЫ ПРОБЛЕМ ===");
        if (detectedIssues.Count == 0)
            sb.AppendLine("Явные сигналы по встроенным эвристикам не обнаружены. Это не доказывает отсутствие проблем с FPS/frametime или драйверами.");
        else
            foreach (string issue in detectedIssues.Distinct()) sb.AppendLine("[ПРОВЕРИТЬ] " + issue);
        sb.AppendLine("\nОграничения: проверки эвристические. Ошибки сетевых интерфейсов могут быть накопительными, WMI не заменяет SMART, пять ping-проб к 1.1.1.1 не измеряют задержку игрового сервера. Для FPS сравнивай frametime в одной сцене через CapFrameX/PresentMon.");
        return sb.ToString();
        });
        var reportDir = Path.Combine(AppContext.BaseDirectory, "Отчёты");
        Directory.CreateDirectory(reportDir);
        var file = Path.Combine(reportDir, $"Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(file, report, Encoding.UTF8);
        Log($"Отчёт сохранён: {file}");
        using var view = new ReportForm(report);
        view.ShowDialog(this);
    }

    private void ApplyGameMode()
    {
        if (!Confirm("Включить Game Mode для текущего пользователя? Эффект зависит от игры и не гарантирует прирост FPS.")) throw new OperationCanceledException();
        SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, "Игровой режим включён", false, false);
        SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AllowAutoGameMode", 1, "Автоматический игровой режим включён", false, false);
        MessageBox.Show("Настройки Game Mode записаны. Перезапусти игру и сравни поведение в одинаковой сцене.", "Игровой режим", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetGameMode()
    {
        if (!Confirm("Удалить параметры Game Mode, установленные оптимизатором? Windows вернётся к своему управлению этими значениями.")) throw new OperationCanceledException();
        DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", "Игровой режим", false, false);
        DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AllowAutoGameMode", "Автоматический игровой режим", false, false);
        MessageBox.Show("Параметры Game Mode сброшены.", "Игровой режим", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ApplyGameDvr()
    {
        if (!Confirm("Отключить фоновую запись игрового процесса для текущего пользователя? Xbox Game Bar может перестать сохранять клипы.")) throw new OperationCanceledException();
        SetDword(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0, "Фоновая запись игр выключена", false, false);
        SetDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Захват приложений выключен", false, false);
        MessageBox.Show("Параметры фонового захвата записаны.", "Game DVR", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetGameDvr()
    {
        if (!Confirm("Удалить параметры фоновой записи, установленные оптимизатором?")) throw new OperationCanceledException();
        DeleteRegistryValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", "Game DVR", false, false);
        DeleteRegistryValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", "Захват приложений", false, false);
        MessageBox.Show("Параметры Game DVR сброшены.", "Game DVR", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ConfigureDefenderScanCpu(int percent)
    {
        if (percent < 5 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        var command = $"try {{ Set-MpPreference -ScanAvgCPULoadFactor {percent} -ErrorAction Stop; Write-Output 'OK' }} catch {{ Write-Error $_; exit 1 }}";
        var result = RunCommand("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
        if (!result.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Не удалось изменить лимит нагрузки Defender. Запусти программу от имени администратора и проверь, что Microsoft Defender доступен.\n" + result);
        }
        MessageBox.Show($"Средняя загрузка CPU при сканировании Defender установлена на {percent}%. Защита в реальном времени остаётся включённой.", "Microsoft Defender", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool RegexLike(string line, string pattern) =>
        System.Text.RegularExpressions.Regex.IsMatch(line, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string RunCommand(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (p is null) return "(Не удалось запустить команду)";

            // Read both streams asynchronously to avoid pipe deadlocks on verbose commands.
            Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return "(Тайм-аут команды: 8 секунд; процесс остановлен)";
            }
            Task.WaitAll(stdoutTask, stderrTask);
            string stdout = stdoutTask.Result.Trim();
            string stderr = stderrTask.Result.Trim();
            string combined = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (p.ExitCode != 0)
                return $"(Код завершения {p.ExitCode})" + (combined.Length > 0 ? Environment.NewLine + combined : string.Empty);
            return combined.Length > 0 ? combined : "(Команда завершилась без вывода; код 0)";
        }
        catch (Exception ex) { return "(Ошибка запуска команды) " + ex.Message; }
    }

    private void OpenReports()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Отчёты");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private static bool Confirm(string message) =>
        MessageBox.Show(message, "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    private static readonly string ConfigDirectory = Path.Combine(AppContext.BaseDirectory, "config");
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "changes.json");
    private static readonly string ToggleStatePath = Path.Combine(ConfigDirectory, "toggle_states.json");
    private static readonly object ConfigLock = new();

    private static Dictionary<string, bool> LoadToggleStates()
    {
        lock (ConfigLock)
        {
            try
            {
                if (!File.Exists(ToggleStatePath)) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(ToggleStatePath))
                    ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); }
        }
    }

    private static bool GetToggleState(string name) => LoadToggleStates().TryGetValue(name, out bool value) && value;

    private static void SaveToggleState(string name, bool enabled)
    {
        lock (ConfigLock)
        {
            Directory.CreateDirectory(ConfigDirectory);
            var data = File.Exists(ToggleStatePath)
                ? (System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(ToggleStatePath)) ?? new())
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            data[name] = enabled;
            File.WriteAllText(ToggleStatePath, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void ClearToggleStates()
    {
        lock (ConfigLock) { if (File.Exists(ToggleStatePath)) File.Delete(ToggleStatePath); }
    }

    private static string NormalizeRegistryPath(string path)
    {
        // Accept older saved/call-site paths with doubled separators; RegistryKey expects a relative subkey path.
        while (path.Contains(@"\\", StringComparison.Ordinal)) path = path.Replace(@"\\", @"\", StringComparison.Ordinal);
        return path.TrimStart('\\');
    }

    private static (RegistryKey Hive, string SubPath) ResolveRegistryPath(string path)
    {
        path = NormalizeRegistryPath(path);
        if (path.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, path.Substring(@"HKEY_CURRENT_USER\".Length).TrimStart('\\'));
        if (path.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, path.Substring(@"HKEY_LOCAL_MACHINE\".Length).TrimStart('\\'));
        if (path.StartsWith(@"HKEY_CLASSES_ROOT\", StringComparison.OrdinalIgnoreCase))
            return (Registry.ClassesRoot, path.Substring(@"HKEY_CLASSES_ROOT\".Length).TrimStart('\\'));
        throw new ArgumentException("Неизвестный корень реестра: " + path);
    }

    private static void RememberChange(string path, string name, string value)
    {
        lock (ConfigLock)
        {
            Directory.CreateDirectory(ConfigDirectory);
            Dictionary<string, string> data = File.Exists(ConfigPath)
                ? (System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigPath)) ?? new())
                : new();
            data[NormalizeRegistryPath(path) + "|" + name] = value;
            File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void ForgetChange(string path, string name)
    {
        lock (ConfigLock)
        {
            if (!File.Exists(ConfigPath)) return;
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigPath)) ?? new();
            data.Remove(NormalizeRegistryPath(path) + "|" + name);
            File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void ApplyHardProfile()
    {
        // Conservative profile. One confirmation is shown by the button; each setting is attempted and failures are reported.
        var failures = new List<string>();
        void TryStep(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Log("HARD: ошибка — " + name + ": " + ex.Message); }
        }
        TryStep("Delivery Optimization", () => SetDword(@"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode", 0, "HARD: P2P-раздача обновлений отключена", false, false));
        TryStep("Telemetry policy", () => SetDword(@"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 0, "HARD: ограничение диагностических данных", false, false));
        TryStep("Power Throttling", () => SetDword(@"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff", 1, "HARD: Power Throttling отключён", false, false));
        TryStep("Background apps", () => SetDword(@"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", 1, "HARD: фоновые приложения ограничены", false, false));
        TryStep("Game DVR", () => SetDword(@"HKEY_CURRENT_USER\\System\\GameConfigStore", "GameDVR_Enabled", 0, "HARD: Game DVR отключён", false, false));
        TryStep("App capture", () => SetDword(@"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", 0, "HARD: захват приложений отключён", false, false));
        TryStep("Power plan", () => SetPowerPlan("SCHEME_MAX", "Высокая производительность"));
        if (failures.Count > 0)
            throw new InvalidOperationException("Профиль применён частично. Не удалось применить:\n• " + string.Join("\n• ", failures));
        Log("HARD: профиль применён; план «Высокая производительность» включён. Защита Windows и критические службы не отключались.");
    }

    private void ResetHardProfile()
    {
        var failures = new List<string>();
        void TryStep(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Log("Сброс HARD: ошибка — " + name + ": " + ex.Message); }
        }
        TryStep("Delivery Optimization", () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode", "HARD: P2P-раздача обновлений", false, false));
        TryStep("Telemetry policy", () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", "HARD: диагностические данные", false, false));
        TryStep("Power Throttling", () => DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff", "HARD: Power Throttling", false, false));
        TryStep("Background apps", () => DeleteRegistryValue(@"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", "HARD: фоновые приложения", false, false));
        TryStep("Game DVR", () => DeleteRegistryValue(@"HKEY_CURRENT_USER\\System\\GameConfigStore", "GameDVR_Enabled", "HARD: Game DVR", false, false));
        TryStep("App capture", () => DeleteRegistryValue(@"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", "HARD: захват приложений", false, false));
        TryStep("Power plan", () => SetPowerPlan("SCHEME_BALANCED", "Сбалансированный"));
        if (failures.Count > 0)
            throw new InvalidOperationException("Сброс выполнен частично. Проверь ошибки:\n• " + string.Join("\n• ", failures));
        Log("HARD: значения профиля удалены; активирован сбалансированный план питания. Перезагрузка может потребоваться.");
    }

    private void SetPowerPlan(string scheme, string label)
    {
        string output = RunCommand("powercfg.exe", "/setactive " + scheme);
        if (output.Contains("Код завершения", StringComparison.OrdinalIgnoreCase) || output.Contains("Тайм-аут", StringComparison.OrdinalIgnoreCase) || output.Contains("Ошибка запуска", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не удалось переключить план питания «" + label + "»: " + output);
        string active = RunCommand("powercfg.exe", "/getactivescheme");
        string expectedGuid = scheme.Equals("SCHEME_BALANCED", StringComparison.OrdinalIgnoreCase)
            ? "381b4222-f694-41f0-9685-ff5bb260df2e"
            : scheme.Equals("SCHEME_MAX", StringComparison.OrdinalIgnoreCase)
                ? "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"
                : string.Empty;
        if (expectedGuid.Length > 0 && !active.Contains(expectedGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows не подтвердила план «" + label + "». Текущий план: " + active);
        Log("План питания установлен/проверен: " + label + ". " + active.Trim());
    }

    private void SetDword(string keyPath, string valueName, int value, string label, bool askConfirmation = true, bool showSuccess = true)
    {
        if (askConfirmation && !Confirm($"Применить параметр «{label}»?\n\nИзменение будет записано в реестр Windows. Автоматическая резервная копия не создаётся. Некоторые параметры требуют перезагрузки и могут не улучшить FPS."))
            throw new OperationCanceledException();

        var (hive, subPath) = ResolveRegistryPath(keyPath);
        using var key = hive.CreateSubKey(subPath, true);
        if (key is null) throw new InvalidOperationException("Не удалось открыть раздел реестра.");
        key.SetValue(valueName, value, RegistryValueKind.DWord);
        RememberChange(keyPath, valueName, value.ToString());
        Log($"{label}: параметр {valueName} установлен. Может потребоваться перезагрузка.");
        if (showSuccess)
            MessageBox.Show($"{label}: настройка применена.\n\nАвтоматическая резервная копия не создавалась. Для отмены используй кнопку сброса соответствующей настройки. Возможно, потребуется перезагрузка.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DeleteRegistryValue(string keyPath, string valueName, string label, bool askConfirmation = true, bool showSuccess = true)
    {
        if (askConfirmation && !Confirm($"Удалить параметр {valueName} из реестра? Это вернёт управление настройкой Windows к поведению по умолчанию, но не обязательно восстановит ранее заданное вручную значение."))
            throw new OperationCanceledException();

        var (hive, subPath) = ResolveRegistryPath(keyPath);
        using var key = hive.OpenSubKey(subPath, true);
        if (key is not null) key.DeleteValue(valueName, false);
        ForgetChange(keyPath, valueName);
        Log($"{label}: параметр сброшен к системному поведению по умолчанию.");
        if (showSuccess)
            MessageBox.Show("Параметр удалён. Для некоторых изменений потребуется перезагрузка.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static IEnumerable<Control> ControlsRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            yield return c;
            foreach (var child in ControlsRecursive(c)) yield return child;
        }
    }

    private void ClearSystemTempFiles()
    {
        if (!Confirm("Удалить доступные файлы из системного TEMP? Заблокированные файлы будут пропущены. Продолжить?")) return;
        string temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        int removed = 0, skipped = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(temp))
                try { File.Delete(file); removed++; } catch { skipped++; }
            foreach (string dir in Directory.EnumerateDirectories(temp))
                try { Directory.Delete(dir, true); removed++; } catch { skipped++; }
        }
        catch (Exception ex) { Log("Системный TEMP: " + ex.Message); }
        Log($"Системный TEMP: удалено объектов {removed}, пропущено {skipped}.");
        MessageBox.Show($"Очистка завершена.\nУдалено объектов: {removed}\nПропущено: {skipped}", "Системный TEMP", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearThumbnailCache()
    {
        if (!Confirm("Удалить кэш миниатюр проводника? Windows создаст его заново. Продолжить?")) return;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        int removed = 0;
        try
        {
            foreach (string file in Directory.GetFiles(local, "thumbcache*.db", SearchOption.TopDirectoryOnly))
                try { File.Delete(file); removed++; } catch { }
        }
        catch (Exception ex) { Log("Кэш миниатюр: " + ex.Message); }
        Log("Удалено файлов кэша миниатюр: " + removed);
        MessageBox.Show("Очистка кэша миниатюр завершена. Часть файлов может быть занята Проводником.", "Очистка", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearDeliveryOptimizationCache()
    {
        if (!Confirm("Очистить кэш оптимизации доставки обновлений через штатную команду Windows? Это не отключает обновления.")) return;
        string output = RunCommand("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"try { Import-Module DeliveryOptimization -ErrorAction Stop; Delete-DeliveryOptimizationCache -Force -ErrorAction Stop; Write-Output 'OK' } catch { Write-Error $_; exit 1 }\"");
        bool succeeded = string.Equals(output.Trim(), "OK", StringComparison.OrdinalIgnoreCase);
        Log("Кэш доставки: " + output.Trim());
        MessageBox.Show(succeeded ? "Штатная команда Windows очистила кэш доставки." : "Windows не подтвердила очистку кэша доставки. Возможно, модуль недоступен или нужны права администратора.\n\n" + output, "Очистка", MessageBoxButtons.OK, succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void CleanOptimizerMemory()
    {
        try
        {
            long before = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long after = GC.GetTotalMemory(true);
            long freed = Math.Max(0, before - after);
            Log($"Мягкая очистка памяти оптимизатора: освобождено примерно {freed / 1024:N0} КБ управляемой памяти процесса.");
            MessageBox.Show(
                $"Очистка памяти оптимизатора завершена.\nОсвобождено примерно: {freed / 1024:N0} КБ.\n\nЭто касается только самого оптимизатора, а не Dota 2 или всей оперативной памяти Windows. Windows автоматически управляет системным кэшем; принудительная очистка может ухудшить плавность игр.",
                "Очистка памяти", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Ошибка мягкой очистки памяти оптимизатора: " + ex.Message);
            MessageBox.Show("Не удалось выполнить очистку памяти оптимизатора: " + ex.Message, "Очистка памяти", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearWerReports()
    {
        if (!Confirm("Удалить доступные локальные отчёты об ошибках Windows? Это не исправляет причину сбоев и удаляет диагностические материалы.")) return;
        string[] dirs = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportQueue")
        };
        int removed = 0, skipped = 0;
        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string child in Directory.GetDirectories(dir))
                    try { Directory.Delete(child, true); removed++; } catch { skipped++; }
                foreach (string file in Directory.GetFiles(dir))
                    try { File.Delete(file); removed++; } catch { skipped++; }
            }
            catch { skipped++; }
        }
        Log($"WER: удалено объектов {removed}, пропущено {skipped}.");
        MessageBox.Show($"Очистка отчётов завершена.\nУдалено: {removed}\nПропущено: {skipped}", "Очистка", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearTempFiles()
    {
        if (!Confirm("Удалить доступные файлы из TEMP текущего пользователя? Заблокированные файлы будут пропущены. Продолжить?")) return;
        string temp = Path.GetTempPath();
        int removed = 0, skipped = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(temp))
                try { File.Delete(file); removed++; } catch { skipped++; }
            foreach (string dir in Directory.EnumerateDirectories(temp))
                try { Directory.Delete(dir, true); removed++; } catch { skipped++; }
        }
        catch (Exception ex) { Log("TEMP: " + ex.Message); }
        Log($"Очистка TEMP завершена. Удалено объектов: {removed}; пропущено: {skipped}.");
        MessageBox.Show($"Очистка TEMP завершена.\nУдалено объектов: {removed}\nПропущено/заблокировано: {skipped}", "Очистка", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearRecycleBin()
    {
        if (!Confirm("Очистить корзину Windows? Файлы будут удалены окончательно. Продолжить?")) return;
        string output = RunCommand("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"try { Clear-RecycleBin -Force -ErrorAction Stop; Write-Output 'OK' } catch { Write-Error $_; exit 1 }\"");
        bool succeeded = string.Equals(output.Trim(), "OK", StringComparison.OrdinalIgnoreCase);
        Log("Очистка корзины: " + output.Trim());
        MessageBox.Show(succeeded ? "Корзина очищена." : "Windows не подтвердила очистку корзины. Проверь журнал и права доступа.\n\n" + output, "Очистка", MessageBoxButtons.OK, succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ClearShaderCaches()
    {
        if (!Confirm("Удалить содержимое известных кэшей шейдеров DirectX и NVIDIA для текущего пользователя?\n\nКэши будут созданы заново играми и драйвером. После очистки первые запуски могут временно фризить сильнее. Закрой игры перед продолжением. Автоматическая резервная копия не создаётся.")) return;
        var dirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "GLCache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA Corporation", "NV_Cache")
        };
        int removed = 0, skipped = 0;
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); removed++; } catch { skipped++; }
                }
                foreach (var sub in Directory.EnumerateDirectories(dir).OrderByDescending(x => x.Length))
                {
                    try { if (Directory.Exists(sub) && !Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
                }
                Log($"Кэш обработан: {dir}");
            }
            catch (Exception ex) { skipped++; Log($"Не удалось полностью очистить {dir}: {ex.Message}"); }
        }
        Log($"Очистка кэша шейдеров завершена. Удалено файлов: {removed}; пропущено: {skipped}.");
        MessageBox.Show($"Очистка завершена.\nУдалено файлов: {removed}\nПропущено/заблокировано: {skipped}\n\nПервый запуск игр после очистки может сопровождаться временными статтерами, пока шейдеры компилируются заново.", "Кэш шейдеров", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ApplyUltimatePerformance()
    {
        if (!Confirm("Активировать план Ultimate Performance? Он может увеличить энергопотребление и нагрев, а прирост FPS не гарантирован. Автоматическая копия текущего плана не создаётся. Кнопка возврата переключит на стандартный «Сбалансированный» план, а не на ваш предыдущий пользовательский план.")) throw new OperationCanceledException();
        {
            const string ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
            string list = RunCommand("powercfg.exe", "/list");
            string guid = ultimate;
            if (!list.Contains(ultimate, StringComparison.OrdinalIgnoreCase))
            {
                string created = RunCommand("powercfg.exe", "/duplicatescheme " + ultimate);
                var match = System.Text.RegularExpressions.Regex.Match(created, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
                if (!match.Success) throw new InvalidOperationException("Windows не смогла создать план Ultimate Performance. " + created);
                guid = match.Value;
            }
            var output = RunCommand("powercfg.exe", "/setactive " + guid);
            string active = RunCommand("powercfg.exe", "/getactivescheme");
            if (output.Contains("Код завершения", StringComparison.OrdinalIgnoreCase) || output.Contains("Тайм-аут", StringComparison.OrdinalIgnoreCase) || !active.Contains(guid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Не удалось подтвердить активацию Ultimate Performance. Команда: " + output + Environment.NewLine + "Текущий план: " + active);
            Log("Активирован план Ultimate Performance. " + active.Trim());
            MessageBox.Show("План питания переключён. Прирост производительности не гарантирован; следи за температурой и энергопотреблением.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void RestoreBalanced()
    {
        if (!Confirm("Переключить на стандартный план «Сбалансированный»? Это не восстановит ранее выбранный пользовательский план.")) throw new OperationCanceledException();
        SetPowerPlan("SCHEME_BALANCED", "Сбалансированный");
        string active = RunCommand("powercfg.exe", "/getactivescheme");
        if (!active.Contains("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не удалось подтвердить план «Сбалансированный». Текущий план: " + active);
        MessageBox.Show("Активирован план «Сбалансированный».", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FlushDnsCache()
    {
        if (!Confirm("Очистить локальный DNS-кэш Windows? Это может помочь при устаревших DNS-записях, но обычно не снижает игровой пинг. Продолжить?")) return;
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"),
                Arguments = "/flushdns",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            proc.Start();
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);
            string output = (stdout + Environment.NewLine + stderr).Trim();
            if (proc.ExitCode == 0)
            {
                Log("DNS-кэш успешно очищен. " + output);
                MessageBox.Show("DNS-кэш успешно очищен. Это помогает при устаревших DNS-записях, но обычно не снижает пинг в игре.", "DNS-кэш", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log("Ошибка очистки DNS-кэша (код " + proc.ExitCode + "): " + output);
                MessageBox.Show("Windows не подтвердила очистку DNS-кэша. Запусти программу от имени администратора и попробуй снова.\n\n" + output, "Ошибка DNS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Log("Ошибка очистки DNS-кэша: " + ex.Message);
            MessageBox.Show("Не удалось выполнить ipconfig /flushdns.\n\n" + ex.Message, "Ошибка DNS", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyVisualEffects()
    {
        if (!Confirm("Уменьшить анимации меню и окон Windows для текущего пользователя? Это делает интерфейс отзывчивее, но не гарантирует рост FPS в играх.")) throw new OperationCanceledException();
        using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop\WindowMetrics", true))
            key?.SetValue("MinAnimate", "0", RegistryValueKind.String);
        using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
            key?.SetValue("MenuShowDelay", "0", RegistryValueKind.String);
        Log("Параметры анимаций применены. Для полного эффекта может потребоваться выход из учётной записи.");
        MessageBox.Show("Анимации окон уменьшены, задержка меню установлена в 0. Для полного эффекта выйди из системы и войди снова.", "Анимации Windows", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetVisualEffects()
    {
        if (!Confirm("Удалить параметры анимаций, заданные программой? Windows вернётся к системному поведению по умолчанию; прежние значения не сохранялись.")) throw new OperationCanceledException();
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics", true))
            key?.DeleteValue("MinAnimate", false);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            key?.DeleteValue("MenuShowDelay", false);
        Log("Параметры анимаций удалены; используется поведение Windows по умолчанию.");
    }

    private void OpenStartupManager()
    {
        using var form = new StartupManagerForm(Log);
        form.ShowDialog(this);
    }

    private void SetGamePriority(string[] processNames, string displayName)
    {
        var found = new List<Process>();
        foreach (string name in processNames)
            found.AddRange(Process.GetProcessesByName(name));
        if (found.Count == 0)
        {
            MessageBox.Show($"Процесс {displayName} не найден. Сначала запусти игру, затем нажми кнопку ещё раз.", "Игра не запущена", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int changed = 0;
        foreach (var process in found)
        {
            try { process.PriorityClass = ProcessPriorityClass.AboveNormal; changed++; }
            catch (Exception ex) { Log($"Не удалось изменить приоритет {process.ProcessName}: {ex.Message}"); }
            finally { process.Dispose(); }
        }
        Log($"{displayName}: приоритет AboveNormal временно применён к процессам: {changed}. После закрытия и повторного запуска игры Windows вернёт обычный приоритет.");
        MessageBox.Show($"Готово. Изменено процессов: {changed}. Это временно меняет приоритет только у запущенного процесса; после перезапуска игры настройка сбросится.", displayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenSteamValidation(string appId)
    {
        if (!Confirm("Открыть проверку файлов игры в Steam? Steam может начать проверку/загрузку файлов.")) return;
        Process.Start(new ProcessStartInfo { FileName = $"steam://validate/{appId}", UseShellExecute = true });
        Log($"Запрошена проверка файлов Steam AppID {appId}.");
    }

    private void ClearGameShaderCache(string appId, string displayName)
    {
        if (Process.GetProcessesByName(appId == "570" ? "dota2" : "cs2").Length > 0)
        {
            MessageBox.Show("Сначала полностью закрой игру, затем повтори очистку.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Confirm($"Удалить локальный кэш шейдеров {displayName}? Он будет создан заново, первые запуски могут подтормаживать.")) return;
        string steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        string cache = Path.Combine(steam, "steamapps", "shadercache", appId);
        if (!Directory.Exists(cache))
        {
            MessageBox.Show($"Папка кэша не найдена:\n{cache}\nЕсли Steam установлен в другой папке, функция пока не найдёт этот кэш автоматически.", "Кэш не найден", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int deleted = 0;
        foreach (string file in Directory.GetFiles(cache, "*", SearchOption.AllDirectories))
            try { File.Delete(file); deleted++; } catch { }
        foreach (string dir in Directory.GetDirectories(cache, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            try { Directory.Delete(dir, false); } catch { }
        Log($"{displayName}: удалено файлов кэша {deleted}.");
        MessageBox.Show($"Очистка завершена. Удалено файлов: {deleted}.", displayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DisableAutoUpdates()
    {
        if (!Confirm("Отключить автоматическое получение/установку обновлений политикой Windows? Это повышает риск пропустить исправления безопасности. Службы не отключаются; результат зависит от редакции Windows. Рекомендуется использовать только временно. Продолжить?")) return;
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true))
            key?.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
        Log("Установлена политика NoAutoUpdate=1. Проверь действие в Windows Update; политика может не работать одинаково во всех редакциях.");
        MessageBox.Show("Политика автоматических обновлений установлена. Это не отключает все механизмы обновления. Для безопасности регулярно устанавливай обновления вручную; сбрось эту настройку после использования.", "Обновления Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ResetAutoUpdates()
    {
        if (!Confirm("Удалить политику NoAutoUpdate и вернуть стандартное управление обновлениями Windows?")) return;
        DeleteRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "Политика автообновлений", false, true);
    }

    private void ApplyStutterProfile()
    {
        if (!Confirm("Применить профиль игровой отзывчивости?\n\nВключит Windows Game Mode и отключит Power Throttling для фоновых приложений. Спорные параметры MMCSS не меняются. Эффект не гарантирован; при ухудшении используй «Сбросить».")) throw new OperationCanceledException();
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar", true))
        {
            key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
            key?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
        }
        using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true))
            key?.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
        // Avoid undocumented MMCSS registry recipes: their benefit is not established and may be counterproductive.
        Log("Профиль игровой отзывчивости применён: Game Mode и Power Throttling OFF. Не менялись спорные MMCSS-параметры. Перезагрузка рекомендуется.");
        MessageBox.Show("Профиль применён: Game Mode и Power Throttling. Спорные MMCSS-параметры не изменялись. Прирост FPS не гарантирован; сравни одинаковую сцену до/после, а при ухудшении нажми «Сбросить».", "Игровая отзывчивость", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetStutterProfile()
    {
        if (!Confirm("Удалить параметры профиля игровой отзывчивости? Будут удалены Game Mode и PowerThrottlingOff, а также старые MMCSS-параметры, если они остались от прежних версий. Исходные значения ранее не сохранялись.")) throw new OperationCanceledException();
        var failures = new List<string>();
        void TryReset(string label, Action action) { try { action(); } catch (Exception ex) { failures.Add(label + ": " + ex.Message); } }
        TryReset("Game Mode", () => { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true); key?.DeleteValue("AutoGameModeEnabled", false); key?.DeleteValue("AllowAutoGameMode", false); });
        TryReset("Power Throttling", () => { using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true); key?.DeleteValue("PowerThrottlingOff", false); });
        TryReset("Старые MMCSS", () => { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true); key?.DeleteValue("SystemResponsiveness", false); });
        TryReset("Старые MMCSS Games", () => { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true); key?.DeleteValue("GPU Priority", false); key?.DeleteValue("Priority", false); key?.DeleteValue("Scheduling Category", false); key?.DeleteValue("SFIO Priority", false); });
        if (failures.Count > 0) throw new InvalidOperationException("Сброс профиля выполнен частично:\n• " + string.Join("\n• ", failures));
        Log("Профиль игровой отзывчивости сброшен: параметры профиля удалены.");
    }

    private void ResetAllChanges()
    {
        if (!Confirm("Сбросить изменения, которые эта программа могла внести?\n\nБудут удалены её известные параметры реестра, выбран сбалансированный план питания, удалены параметры анимаций и восстановлены записи автозапуска с суффиксом .DisabledByGamingOptimizer / .disabled.\n\nАвтоматической резервной копии нет. Невозможно восстановить исходные значения, которые программа ранее не сохраняла. Продолжить?")) return;
        var values = new (string Path, string Name)[]
        {
            (@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
            (@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
            (@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
            (@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate")
        };
        foreach (var v in values)
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(v.Path, true); k?.DeleteValue(v.Name, false); }
            catch (Exception ex) { Log($"Не удалось сбросить {v.Name}: {ex.Message}"); }
        }
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics", true); k?.DeleteValue("MinAnimate", false); } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true); k?.DeleteValue("MenuShowDelay", false); } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", true); k?.DeleteValue("GlobalUserDisabled", false); } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", true); k?.DeleteValue("AppCaptureEnabled", false); k?.DeleteValue("AudioCaptureEnabled", false); } catch {}
        // Return services disabled by this optimizer to automatic start; no original service configuration was backed up.
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true); k?.SetValue("Start", 2, RegistryValueKind.DWord); } catch (Exception ex) { Log("Сброс SysMain: " + ex.Message); }
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch", true); k?.SetValue("Start", 2, RegistryValueKind.DWord); } catch (Exception ex) { Log("Сброс Windows Search: " + ex.Message); }
        try
        {
            string defenderReset = RunCommand("powershell.exe", "-NoProfile -Command \"try { Set-MpPreference -ScanAvgCPULoadFactor 50 -ErrorAction Stop; 'OK' } catch { Write-Error $_; exit 1 }\"");
            if (!defenderReset.Contains("OK", StringComparison.OrdinalIgnoreCase)) Log("Сброс лимита сканирования Defender не подтверждён: " + defenderReset);
            else Log("Лимит среднего CPU Defender установлен на 50% (штатное значение по документации Microsoft).");
        }
        catch (Exception ex) { Log("Сброс лимита Defender: " + ex.Message); }
        // Restore GameBar/GameDVR values to Windows defaults.
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", true);
            k?.DeleteValue("VisualFXSetting", false);
        } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameBar", true); k?.DeleteValue("AutoGameModeEnabled", false); k?.DeleteValue("AllowAutoGameMode", false); } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", true); k?.DeleteValue("GameDVR_Enabled", false); k?.DeleteValue("GameDVR_FSEBehaviorMode", false); } catch {}
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true); k?.DeleteValue("AutoGameModeEnabled", false); k?.DeleteValue("AllowAutoGameMode", false); } catch {}
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true); k?.DeleteValue("SystemResponsiveness", false); } catch {}
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true); k?.DeleteValue("GPU Priority", false); k?.DeleteValue("Priority", false); k?.DeleteValue("Scheduling Category", false); k?.DeleteValue("SFIO Priority", false); } catch {}
        RunCommand("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
        RestoreDisabledStartupEntries();
        try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); }
        catch (Exception ex) { Log("Не удалось очистить журнал конфигурации: " + ex.Message); }
        Log("Выполнен общий сброс известных настроек программы. Файл конфигурации изменений очищен; журнал действий сохранён для аудита. Проверь сообщения об ошибках и перезагрузи ПК при необходимости.");
        ClearToggleStates();
        foreach (Control control in ControlsRecursive(this))
            if (control is Button toggleButton && toggleButton.Tag is Action<bool> resetToggleVisual)
                resetToggleVisual(false);
        MessageBox.Show("Сброс известных настроек выполнен. Некоторые параметры могут потребовать выхода из системы или перезагрузки. Настройки, которые программа не сохраняла до изменения, нельзя восстановить в исходное состояние.", "Общий сброс", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreDisabledStartupEntries()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var disabled = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run.DisabledByGamingOptimizer", false);
                if (disabled == null) continue;
                using var target = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                foreach (var name in disabled.GetValueNames())
                {
                    if (target.GetValue(name) == null)
                        target.SetValue(name, disabled.GetValue(name)!, disabled.GetValueKind(name));
                }
                hive.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Run.DisabledByGamingOptimizer", false);
            } catch (Exception ex) { Log("Автозапуск: " + ex.Message); }
        }
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.disabled"))
                {
                    var original = file[..^9];
                    if (!File.Exists(original)) File.Move(file, original);
                }
            } catch (Exception ex) { Log("Папка автозапуска: " + ex.Message); }
        }
    }

    private void About()
    {
        MessageBox.Show(
            "Win11 Gaming Optimizer — универсальная утилита диагностики Windows 11.\n" +
            "Автор: cireincroinsea\n\n" +
            "Реестровые твики и планы питания меняются только по нажатию соответствующих кнопок. Резервные копии автоматически не создаются. " +
            "Не отключает Defender, Firewall, Windows Update, службы или VBS/HVCI. Некоторые параметры требуют перезагрузки и не гарантируют прирост FPS. Ограничение телеметрии политикой не означает полного отключения всех диагностических данных.\n\n" +
            "Для микрофризов сравнивай график frametime в одной и той же сцене. Отчёт помогает собрать исходные данные, но не определяет автоматически единственную причину.",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

public sealed class ReportForm : Form
{
    public ReportForm(string report)
    {
        Text = "Отчёт диагностики";
        Width = 900; Height = 680; StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(12, 14, 20);
        ForeColor = Color.FromArgb(232, 237, 245);
        var box = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), Text = report, BackColor = Color.FromArgb(18, 21, 29), ForeColor = Color.FromArgb(232, 237, 245), BorderStyle = BorderStyle.None };
        box.HandleCreated += (_, _) => { try { DarkScrollBar.Apply(box.Handle, true); } catch { } };
        Controls.Add(box);
    }
}


public static class DarkScrollBar
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    public static void Apply(IntPtr handle, bool dark = true)
    {
        // Native Windows scrollbars follow the selected dark/light mode where supported.
        // Windows does not expose a reliable public API for arbitrary accent-colored scrollbars.
        try { SetWindowTheme(handle, dark ? "DarkMode_Explorer" : null, null); } catch { }
        try { System.Runtime.InteropServices.Marshal.GetLastWin32Error(); } catch { }
    }
}

public static class AcrylicBackdrop
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void Enable(IntPtr handle)
    {
        // ACCENT_ENABLE_ACRYLICBLURBEHIND; ARGB tint, dark translucent acrylic.
        var policy = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = unchecked((int)0xCC10141D), AnimationId = 0 };
        Apply(handle, policy);
    }

    public static void Disable(IntPtr handle)
    {
        var policy = new AccentPolicy { AccentState = 0, AccentFlags = 0, GradientColor = 0, AnimationId = 0 };
        Apply(handle, policy);
    }

    private static void Apply(IntPtr handle, AccentPolicy policy)
    {
        IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<AccentPolicy>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = System.Runtime.InteropServices.Marshal.SizeOf<AccentPolicy>() };
            SetWindowCompositionAttribute(handle, ref data);
        }
        catch { }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
    }
}

public sealed class BorderlessTabControl : TabControl
{
    private const int TcmAdjustRect = 0x1328;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style &= ~0x00800000; // WS_BORDER
            cp.ExStyle &= ~0x00000200; // WS_EX_CLIENTEDGE
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Fill the entire tab header (including unused space to the right) dark.
        // Do not paint over sibling tab buttons here; that caused them to turn white.
        if (m.Msg == 0x0014) // WM_ERASEBKGND
        {
            var hdc = m.WParam;
            using var g = System.Drawing.Graphics.FromHdc(hdc);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(8, 10, 14));
            g.FillRectangle(brush, ClientRectangle);
            m.Result = (IntPtr)1;
            return;
        }
        // Expand the page display rectangle to cover the native light frame.
        if (m.Msg == TcmAdjustRect && m.WParam == IntPtr.Zero && m.LParam != IntPtr.Zero)
        {
            base.WndProc(ref m);
            var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeRect>(m.LParam);
            rect.Left -= 4;
            rect.Top -= 8;
            rect.Right += 4;
            rect.Bottom += 4;
            System.Runtime.InteropServices.Marshal.StructureToPtr(rect, m.LParam, false);
            return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public sealed class StartupManagerForm : Form
{
    private readonly ListView items = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, CheckBoxes = false };
    private readonly Label info = new() { Dock = DockStyle.Bottom, Height = 48, Text = "Выбери элемент и нажми «Отключить». Это удаляет/переименовывает запись автозапуска; само приложение не удаляется.", TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<StartupItem> entries = new();
    private readonly Action<string>? journal;

    public StartupManagerForm(Action<string>? journal = null)
    {
        this.journal = journal;
        Text = "Программы автозапуска";
        Width = 900; Height = 580; MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(12, 14, 20);
        ForeColor = Color.FromArgb(232, 237, 245);
        Font = new Font("Segoe UI", 9F);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 7, 4, 4), FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.FromArgb(12, 14, 20) };
        Button MakeButton(string text, int width, Color color) => new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = Color.White, UseVisualStyleBackColor = false, Margin = new Padding(0, 0, 8, 0) };
        var refresh = MakeButton("Обновить список", 140, Color.FromArgb(45, 108, 223));
        refresh.Click += (_, _) => { LoadItems(); journal?.Invoke("Менеджер автозагрузки: список обновлён."); };
        var disable = MakeButton("Отключить", 110, Color.FromArgb(150, 42, 50));
        disable.Click += (_, _) => ToggleSelected(false);
        var enable = MakeButton("Включить", 110, Color.FromArgb(39, 112, 75));
        enable.Click += (_, _) => ToggleSelected(true);
        top.Controls.Add(refresh); top.Controls.Add(disable); top.Controls.Add(enable);
        items.HandleCreated += (_, _) => { try { DarkScrollBar.Apply(items.Handle, true); } catch { } };
        items.BackColor = Color.FromArgb(18, 21, 29);
        items.ForeColor = Color.FromArgb(232, 237, 245);
        items.BorderStyle = BorderStyle.FixedSingle;
        items.FullRowSelect = true;
        items.HideSelection = false;
        items.Columns.Add("Программа", 220); items.Columns.Add("Источник", 160); items.Columns.Add("Команда / путь", 470);
        info.BackColor = Color.FromArgb(12, 14, 20);
        info.ForeColor = Color.FromArgb(170, 185, 204);
        info.Padding = new Padding(10, 0, 10, 0);
        Controls.Add(items); Controls.Add(info); Controls.Add(top);
        LoadItems();
    }

    private void LoadItems()
    {
        entries.Clear(); items.Items.Clear();
        AddRunEntries(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run", false);
        AddRunEntries(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run", false);
        AddRunEntries(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run (отключено)", true);
        AddRunEntries(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (отключено)", true);
        AddStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка пользователя");
        AddStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Общая папка");
        foreach (var e in entries)
        {
            var row = new ListViewItem(e.Name);
            row.SubItems.Add(e.Source);
            row.SubItems.Add(e.Command);
            row.Tag = e;
            if (e.Disabled)
            {
                row.BackColor = Color.FromArgb(112, 35, 42);
                row.ForeColor = Color.White;
            }
            else
            {
                row.BackColor = Color.FromArgb(29, 48, 38);
                row.ForeColor = Color.FromArgb(232, 237, 245);
            }
            items.Items.Add(row);
        }
    }

    private void AddRunEntries(RegistryKey hive, string path, string source, bool disabled)
    {
        string actualPath = disabled ? path + ".DisabledByGamingOptimizer" : path;
        try
        {
            using var key = hive.OpenSubKey(actualPath, false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                entries.Add(new StartupItem { Name = name, Source = source, Command = Convert.ToString(key.GetValue(name)) ?? "", Kind = "Registry", Hive = hive.Name, KeyPath = actualPath, ValueName = name, Disabled = disabled });
        }
        catch { }
    }

    private void AddStartupFolder(string folder, string source)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder))
            {
                bool disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                entries.Add(new StartupItem { Name = Path.GetFileName(file), Source = source + (disabled ? " (отключено)" : ""), Command = file, Kind = "File", KeyPath = file, Disabled = disabled });
            }
        }
        catch { }
    }

    private void ToggleSelected(bool enable)
    {
        if (items.SelectedItems.Count == 0) { MessageBox.Show("Сначала выбери программу в списке."); return; }
        var e = items.SelectedItems[0].Tag as StartupItem;
        if (e is null) return;
        if (e.Kind == "Registry")
        {
            try
            {
                RegistryKey hive = e.Hive.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;
                string original = e.KeyPath.Replace(".DisabledByGamingOptimizer", "");
                if (!enable && !e.Disabled)
                {
                    using var source = hive.OpenSubKey(original, false);
                    object? value = source?.GetValue(e.ValueName);
                    if (value is null) throw new InvalidOperationException("Не удалось прочитать запись автозапуска.");
                    RegistryValueKind kind = source!.GetValueKind(e.ValueName);
                    using var target = hive.CreateSubKey(original + ".DisabledByGamingOptimizer", true);
                    target.SetValue(e.ValueName, value, kind);
                    using var writable = hive.OpenSubKey(original, true);
                    writable?.DeleteValue(e.ValueName, false);
                }
                else if (enable && e.Disabled)
                {
                    using var source = hive.OpenSubKey(e.KeyPath, false);
                    object? value = source?.GetValue(e.ValueName);
                    if (value is null) throw new InvalidOperationException("Не удалось прочитать отключённую запись.");
                    RegistryValueKind kind = source!.GetValueKind(e.ValueName);
                    using var target = hive.CreateSubKey(original, true);
                    target.SetValue(e.ValueName, value, kind);
                    using var writable = hive.OpenSubKey(e.KeyPath, true);
                    writable?.DeleteValue(e.ValueName, false);
                }
                else { MessageBox.Show("Выбранная запись уже в нужном состоянии."); return; }
                LoadItems();
                journal?.Invoke($"Автозагрузка: {(enable ? "включена" : "отключена")} запись «{e.Name}» ({e.Source}).");
            }
            catch (Exception ex) { journal?.Invoke($"Ошибка изменения автозагрузки «{e.Name}»: {ex.Message}"); MessageBox.Show("Не удалось изменить запись. Для системных элементов может потребоваться запуск от имени администратора.\n\n" + ex.Message); }
        }
        else if (e.Kind == "File")
        {
            try
            {
                string disabledPath = e.KeyPath + ".disabled";
                if (!enable && !e.Disabled) File.Move(e.KeyPath, disabledPath, true);
                else if (enable && e.Disabled) File.Move(e.KeyPath, e.KeyPath[..^9], true);
                else { MessageBox.Show("Выбранная запись уже в нужном состоянии."); return; }
                LoadItems();
                journal?.Invoke($"Автозагрузка: {(enable ? "включён" : "отключён")} файл «{e.Name}» ({e.Source}).");
            }
            catch (Exception ex) { journal?.Invoke($"Ошибка изменения файла автозапуска «{e.Name}»: {ex.Message}"); MessageBox.Show("Не удалось изменить файл автозапуска.\n\n" + ex.Message); }
        }
    }

    private sealed class StartupItem
    {
        public string Name = "", Source = "", Command = "", Kind = "", Hive = "", KeyPath = "", ValueName = "";
        public bool Disabled;
    }
}
