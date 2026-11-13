using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("TokenStats")]
[assembly: System.Reflection.AssemblyProduct("TokenStats")]
[assembly: System.Reflection.AssemblyDescription("TokenStats — статистика лимитов Codex")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]

namespace TokenStats
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--apply-update")) return Updater.Apply(args);
            bool smokeTest = false;
#if TESTS
            smokeTest = args.Contains("--smoke-test");
            L.TestLanguage = args.Contains("--preview-en") ? "en" : "ru";
            if (args.Contains("--preview-en")) System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            if (args.Contains("--preview-light")) WidgetTheme.PreviewLight = true;
            if (args.Contains("--preview-dark")) WidgetTheme.PreviewLight = false;
            if (args.Contains("--self-test")) return Tests.Run();
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Contains("--install")) return Installation.Install();
                if (args.Contains("--uninstall")) return Installation.Uninstall();
                if (args.Contains("--enable-startup")) { Startup.Set(true); return 0; }
                if (args.Contains("--disable-startup")) { Startup.Set(false); return 0; }
                bool created;
                using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, smokeTest ? "Local\\TokenStats.Show.Smoke" : "Local\\TokenStats.Show"))
                using (var mutex = new Mutex(true, smokeTest ? "Local\\TokenStats.Smoke" : "Local\\TokenStats.Tray", out created))
                {
                    if (!created) { activation.Set(); return 0; }
                    try
                    {
                        using (var context = new TrayApp(smokeTest))
                        using (var smokeTimer = new System.Windows.Forms.Timer())
                        using (var activationTimer = new System.Windows.Forms.Timer { Interval = 200 })
                        {
                            if (smokeTest)
                            {
                                smokeTimer.Interval = 3000;
                                smokeTimer.Tick += delegate { smokeTimer.Stop(); context.ExitThread(); };
                                smokeTimer.Start();
                            }
                            activationTimer.Tick += delegate { if (activation.WaitOne(0)) context.ShowWidget(); };
                            activationTimer.Start();
                            Application.Run(context);
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "TokenStats", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }

    static class Json
    {
        public static Dictionary<string, object> Parse(string value) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(value); }
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static object Get(Dictionary<string, object> value, string key) { object result; return value != null && value.TryGetValue(key, out result) ? result : null; }
        public static Dictionary<string, object> Obj(Dictionary<string, object> value, string key) { return Get(value, key) as Dictionary<string, object>; }
        public static string Str(Dictionary<string, object> value, string key) { return Convert.ToString(Get(value, key)); }
    }

    static class CodexPath
    {
        public static string Find()
        {
            string custom = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-path.txt");
            if (File.Exists(custom))
            {
                string path = File.ReadAllText(custom).Trim().Trim('"');
                if (File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return path;
                throw new Exception("Проверьте путь к codex.exe в codex-path.txt.");
            }
            foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { string path = Path.Combine(part.Trim('"'), "codex.exe"); if (File.Exists(path)) return path; } catch (ArgumentException) { }
            }
            string extensions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
            if (Directory.Exists(extensions))
            {
                foreach (var dir in new DirectoryInfo(extensions).GetDirectories("openai.chatgpt-*").OrderByDescending(d => d.LastWriteTimeUtc))
                {
                    string path = Path.Combine(dir.FullName, "bin", "windows-x86_64", "codex.exe");
                    if (File.Exists(path)) return path;
                }
            }
            throw new Exception(L.T("Codex не найден. Установите Codex или укажите полный путь к codex.exe в файле codex-path.txt рядом с программой.","Codex not found. Install Codex or specify its path in codex-path.txt next to the application."));
        }
    }

    static class LimitsClient
    {
        static Process active;
        public static void Stop()
        {
            var process = active;
            try { if (process != null && !process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        }
        public static async Task<Dictionary<string, object>> Read(string executable)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, "app-server") {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                process.ErrorDataReceived += delegate { }; // Drain stderr without storing account data.
                try
                {
                    process.Start(); active = process; process.BeginErrorReadLine();
                    var operation = Exchange(process);
                    if (await Task.WhenAny(operation, Task.Delay(30000)) != operation)
                    {
                        // Observe the reader failure after shutdown, without keeping a blocked thread alive.
                        ObserveFailure(operation);
                        throw new TimeoutException(L.T("Нет ответа за 30 секунд. Проверьте подключение к интернету.","No response within 30 seconds. Check your connection."));
                    }
                    return await operation;
                }
                finally
                {
                    active = null;
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } } catch (InvalidOperationException) { }
                }
            }
        }
        static void ObserveFailure(Task operation)
        {
            operation.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        static async Task<Dictionary<string, object>> Exchange(Process process)
        {
            await Send(process, new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "TokenStats", version = Updater.CurrentVersion.ToString(3) } } });
            await Response(process, 1);
            await Send(process, new { method = "initialized" });
            await Send(process, new { id = 2, method = "account/rateLimits/read" });
            return await Response(process, 2);
        }
        static async Task Send(Process p, object message)
        {
            await p.StandardInput.WriteLineAsync(Json.Encode(message));
            await p.StandardInput.FlushAsync();
        }
        static async Task<Dictionary<string, object>> Response(Process p, int id)
        {
            while (true)
            {
                string line = await p.StandardOutput.ReadLineAsync();
                if (line == null) throw new Exception(L.T("Codex завершил соединение. Проверьте его установку и вход в аккаунт.","Codex closed the connection. Check installation and sign-in."));
                var message = Json.Parse(line);
                if (Convert.ToString(Json.Get(message, "id")) != id.ToString()) continue;
                var error = Json.Obj(message, "error");
                if (error != null)
                {
                    string detail = Json.Str(error, "message");
                    if (detail.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 || detail.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception(L.T("Нужен вход в Codex через ChatGPT. В меню выберите «Войти через ChatGPT», затем обновите данные.","Sign in to Codex using ChatGPT, then refresh limits."));
                    throw new Exception("Codex: " + detail);
                }
                var result = Json.Obj(message, "result");
                if (result == null) throw new Exception(L.T("Codex вернул ответ без данных.","Codex returned no data."));
                return result;
            }
        }
    }

    sealed class Quota
    {
        public string Bucket, Window, BucketId;
        public int? WindowMinutes;
        public double Remaining;
        public DateTimeOffset? Reset;
        public bool Expired { get { return Reset.HasValue && Reset.Value <= DateTimeOffset.UtcNow; } }
        public static Quota FiveHour(IEnumerable<Quota> rows)
        {
            return rows.FirstOrDefault(q => q.BucketId == "codex" && q.WindowMinutes == 300);
        }
        public static Quota Weekly(IEnumerable<Quota> rows)
        {
            return rows.FirstOrDefault(q => q.BucketId == "codex" && q.WindowMinutes == 10080);
        }
        public static bool PreferWeekly(Dictionary<string, object> result)
        {
            // Business Premium is reported by Codex as self_serve_business_prolite.
            var buckets = Json.Obj(result, "rateLimitsByLimitId");
            var codex = Json.Obj(buckets, "codex");
            string plan = Json.Str(codex, "planType");
            if (string.IsNullOrEmpty(plan))
            {
                var legacy = Json.Obj(result, "rateLimits");
                string id = Json.Str(legacy, "limitId");
                if (string.IsNullOrEmpty(id) || id == "codex") plan = Json.Str(legacy, "planType");
            }
            return string.Equals(plan, "self_serve_business_prolite", StringComparison.OrdinalIgnoreCase);
        }
        public static Quota Primary(IEnumerable<Quota> rows, bool preferWeekly)
        {
            return preferWeekly ? Weekly(rows) : FiveHour(rows);
        }
        public static List<Quota> Parse(Dictionary<string, object> result)
        {
            var rows = new List<Quota>();
            var buckets = Json.Obj(result, "rateLimitsByLimitId");
            if (buckets != null && buckets.Count > 0)
            {
                foreach (var bucket in buckets) Add(rows, bucket.Value as Dictionary<string, object>, bucket.Key);
            }
            else Add(rows, Json.Obj(result, "rateLimits"), "codex");
            return rows;
        }
        static void Add(List<Quota> rows, Dictionary<string, object> bucket, string id)
        {
            if (bucket == null) return;
            string name = Json.Str(bucket, "limitName");
            if (string.IsNullOrEmpty(name)) name = id == "codex" ? "Codex" : id;
            foreach (string key in new[] { "primary", "secondary" })
            {
                var window = Json.Obj(bucket, key);
                if (window == null || Json.Get(window, "usedPercent") == null) continue;
                double used = Convert.ToDouble(Json.Get(window, "usedPercent"));
                if (double.IsNaN(used) || double.IsInfinity(used) || used < 0 || used > 100) throw new Exception(L.T("Некорректный процент лимита в ответе Codex.","Invalid quota percentage returned by Codex."));
                int? minutes = Json.Get(window, "windowDurationMins") == null ? (int?)null : Convert.ToInt32(Json.Get(window, "windowDurationMins"));
                DateTimeOffset? reset = Json.Get(window, "resetsAt") == null ? (DateTimeOffset?)null : new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Convert.ToInt64(Json.Get(window, "resetsAt")));
                rows.Add(new Quota { Bucket = name, BucketId = string.IsNullOrEmpty(Json.Str(bucket, "limitId")) ? id : Json.Str(bucket, "limitId"), WindowMinutes = minutes, Window = WindowName(minutes, key), Remaining = 100 - used, Reset = reset });
            }
        }
        static string WindowName(int? minutes, string key)
        {
            if (!minutes.HasValue) return key == "primary" ? L.T("Основной","Primary") : L.T("Дополнительный","Additional");
            if (minutes == 10080) return L.T("Неделя","Week");
            if (minutes.Value % 1440 == 0) return (minutes.Value / 1440) + L.T(" дн."," d");
            if (minutes.Value % 60 == 0) return (minutes.Value / 60) + L.T(" ч."," h");
            return minutes + L.T(" мин."," min");
        }
    }

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "TokenStats";
        public static bool Enabled { get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && Convert.ToString(key.GetValue(Name)) == Command; } }
        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                key.DeleteValue(Name, false);
                if (enabled) key.SetValue(Name, Command);
            }
        }
    }

    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly QuotaWidget widget;
        readonly ToolStripMenuItem checkUpdates = new ToolStripMenuItem(L.T("Проверить обновления","Check for updates"));
        List<Quota> quotas = new List<Quota>();
        DateTimeOffset? updated;
        string error;
        bool busy, closing, preferWeekly;
        Icon currentIcon;
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        public TrayApp(bool smokeTest = false)
        {
            widget = new QuotaWidget(!smokeTest);
            menu.Renderer = new AppMenuRenderer();
            menu.Font = AppFonts.Create(12, FontStyle.Regular, GraphicsUnit.Pixel);
            menu.ShowImageMargin = true;
            menu.BackColor = WidgetTheme.Canvas; menu.ForeColor = WidgetTheme.Text;
            widget.RefreshRequested += async delegate { await Refresh(); };
            widget.LoginRequested += Login;
            var alwaysVisible = new ToolStripMenuItem(L.T("Всегда показывать виджет","Always show widget")) { Checked = widget.AlwaysVisible, CheckOnClick = true };
            alwaysVisible.CheckedChanged += delegate { widget.SetAlwaysVisible(alwaysVisible.Checked); };
            menu.Items.Add(alwaysVisible);
            var onTop = new ToolStripMenuItem(L.T("Поверх остальных окон","Always on top")) { Checked = widget.Pinned, CheckOnClick = true };
            onTop.CheckedChanged += delegate { widget.SetPinned(onTop.Checked); };
            menu.Items.Add(onTop);
            var snap = new ToolStripMenuItem(L.T("Прикреплять к краю экрана","Snap to screen edges")) { Checked = widget.SnapEnabled, CheckOnClick = true };
            snap.CheckedChanged += delegate { widget.SetSnapEnabled(snap.Checked); };
            menu.Items.Add(snap);
            menu.Items.Add(L.T("Лимиты тарифного плана","Plan usage limits"), null, delegate { OpenUsage(); });
            menu.Items.Add(new ToolStripSeparator());
            var startup = new ToolStripMenuItem(L.T("Запускать вместе с Windows","Launch at Windows startup")) { Checked = Startup.Enabled };
            startup.Click += delegate {
                try { Startup.Set(!Startup.Enabled); startup.Checked = Startup.Enabled; }
                catch (Exception e) { MessageBox.Show(L.T("Не удалось изменить автозапуск: ","Could not change startup setting: ") + e.Message, "TokenStats"); }
            };
            menu.Items.Add(startup);
            checkUpdates.Click += async delegate { await CheckUpdates(); };
            menu.Items.Add(checkUpdates);
            var languageMenu = new ToolStripMenuItem(L.T("Язык", "Language"));
            var russian = new ToolStripMenuItem("Русский") { Checked = L.Russian };
            var english = new ToolStripMenuItem("English") { Checked = !L.Russian };
            Action<string> selectLanguage = delegate(string language) {
                try {
                    L.Select(language);
                    RelabelMenu(menu.Items);
                    russian.Checked=L.Russian; english.Checked=!L.Russian;
                    widget.RefreshLanguage(); Render();
                } catch(Exception e) { MessageBox.Show(e.Message,"TokenStats"); }
            };
            russian.Click += delegate { selectLanguage("ru"); };
            english.Click += delegate { selectLanguage("en"); };
            languageMenu.DropDownItems.Add(russian); languageMenu.DropDownItems.Add(english);
            menu.Items.Add(languageMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Выход","Exit"), null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu; widget.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { Render(); widget.SetEnabled(true); } };
            Render(); tray.Visible = true;
            timer.Interval = 30000; timer.Tick += async delegate { await Refresh(); }; timer.Start();
            var handle = widget.Handle;
            SystemEvents.UserPreferenceChanged += ThemeChanged;
            widget.BeginInvoke(new Action(async delegate {
                widget.PlaceInitial();
#if TESTS
                if (smokeTest)
                {
                    quotas = new List<Quota> {
                        new Quota { BucketId = "codex", WindowMinutes = 300, Remaining = 75, Reset = DateTimeOffset.Now.AddHours(3) },
                        new Quota { BucketId = "codex", WindowMinutes = 10080, Remaining = 42, Reset = DateTimeOffset.Now.AddDays(4) }
                    };
                    updated = DateTimeOffset.Now;
                    widget.SetEnabled(true);
                    foreach (bool premium in new[] { false, true })
                    {
                        preferWeekly = premium; Render();
                        string expectedTooltip = premium ? L.T("Неделя: осталось 42%","Week: remaining 42%") : L.T("5 часов: осталось 75%","5 hours: remaining 75%");
                        if (tray.Text != expectedTooltip) throw new Exception("Tray period/value mismatch: " + tray.Text);
                        if (!widget.AccessibleDescription.Contains(premium ? L.T("42% недельного","42% of weekly") : L.T("75% лимита на 5 часов","75% of 5-hour")))
                            throw new Exception("Tray and widget disagree about selected quota.");
                        foreach (bool expanded in new[] { false, true })
                        {
                            widget.SetExpanded(expanded);
                            using (var preview = new Bitmap(widget.Width, widget.Height))
                            {
                                widget.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                                preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                    "widget-" + (premium ? "premium-" : "") + (expanded ? "expanded.png" : "compact.png")));
                            }
                        }
                    }
                    menu.Show(new Point(20,20));
                    using(var image = new Bitmap(menu.Width,menu.Height)) { menu.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"menu-preview.png")); }
                    menu.Close();
                    widget.SetEnabled(false); return;
                }
#endif
                widget.SetEnabled(true);
                await Refresh();
            }));
        }
        public void ShowWidget()
        {
            if (!closing) { Render(); widget.SetEnabled(true); }
        }
        static void RelabelMenu(ToolStripItemCollection items)
        {
            foreach(ToolStripItem item in items) {
                // Language names keep their native spelling.
                if(item.Text!="English" && item.Text!="Русский") item.Text=L.Relabel(item.Text);
                var parent=item as ToolStripMenuItem;
                if(parent!=null) RelabelMenu(parent.DropDownItems);
            }
        }
        internal static void OpenUsage()
        {
            try { Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage?tab=overview") { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(L.T("Не удалось открыть страницу: ","Could not open page: ") + e.Message, "TokenStats"); }
        }
        void Login()
        {
            try { Process.Start(new ProcessStartInfo(CodexPath.Find(), "login") { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(e.Message, L.T("Вход в Codex","Sign in to Codex")); }
        }
        async Task CheckUpdates()
        {
            if (!checkUpdates.Enabled || closing) return;
            checkUpdates.Enabled = false;
            checkUpdates.Text = L.T("Проверка обновлений…","Checking for updates...");
            string downloaded = null;
            try
            {
                var release = await Updater.Check();
                if (closing) return;
                if (release == null)
                {
                    MessageBox.Show(L.T("На GitHub пока нет опубликованных релизов TokenStats.","No TokenStats releases published on GitHub yet."), L.T("Обновление TokenStats","TokenStats update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (release.Version <= Updater.CurrentVersion)
                {
                    MessageBox.Show(L.T("Установлена актуальная версия TokenStats ","TokenStats is up to date: ") + Updater.CurrentVersion.ToString(3) + ".", L.T("Обновление TokenStats","TokenStats update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(L.T("Доступна версия ","Version available: ") + release.Version.ToString(3) + L.T(". Скачать и установить?\nПриложение автоматически перезапустится.",". Download and install?\nThe application will restart automatically."),
                    L.T("Обновление TokenStats","TokenStats update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                checkUpdates.Text = L.T("Загрузка обновления…","Downloading update...");
                downloaded = await Updater.Download(release);
                if (closing) return;
                Updater.StartInstall(downloaded);
                downloaded = null; // The helper now owns the downloaded executable.
                ExitThread();
            }
            catch (Exception e)
            {
                if (!closing) MessageBox.Show(L.T("Не удалось обновить TokenStats: ","Could not update TokenStats: ") + e.Message, L.T("Обновление TokenStats","TokenStats update"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (downloaded != null) { try { File.Delete(downloaded); Directory.Delete(Path.GetDirectoryName(downloaded)); } catch (IOException) { } }
                if (!closing) { checkUpdates.Enabled = true; checkUpdates.Text = L.T("Проверить обновления","Check for updates"); }
            }
        }
        async Task Refresh()
        {
            if (busy || closing) return;
            busy = true;
            try
            {
                var result = await LimitsClient.Read(CodexPath.Find());
                if (closing) return;
                quotas = Quota.Parse(result);
                preferWeekly = Quota.PreferWeekly(result);
                if (quotas.Count == 0) throw new Exception(L.T("Аккаунт не вернул лимиты. Проверьте вход через ChatGPT.","No quotas returned. Check your ChatGPT sign-in."));
                updated = DateTimeOffset.Now; error = null;
            }
            catch (Exception e) { if (!closing) error = e.Message; }
            finally { busy = false; if (!closing) { timer.Stop(); timer.Interval = error == null ? 30000 : 10000; timer.Start(); widget.RefreshTheme(); menu.BackColor = WidgetTheme.Canvas; menu.ForeColor = WidgetTheme.Text; Render(); } }
        }
        void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (closing || widget.IsDisposed || !widget.IsHandleCreated) return;
            try { widget.BeginInvoke(new Action(() => { if (!closing) { widget.RefreshTheme(); menu.BackColor = WidgetTheme.Canvas; menu.ForeColor = WidgetTheme.Text; Render(); } })); }
            catch (InvalidOperationException) { /* The UI may have closed while the event was queued. */ }
        }
        void Render()
        {
            var selected = Quota.Primary(quotas, preferWeekly);
            bool valid = error == null && selected != null && !selected.Expired;
            double remaining = selected == null ? 0 : selected.Remaining;
            string iconText = valid ? Math.Floor(remaining).ToString() : "?";
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            using (var glyphs = new GraphicsPath())
            using (var family = new FontFamily("Segoe UI"))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);


                // Fit actual glyph outlines, without font padding or a percent sign.
                // Fill the icon with large digits, including the value 100.
                glyphs.AddString(iconText, family, (int)FontStyle.Bold, 32, Point.Empty, StringFormat.GenericTypographic);
                var bounds = glyphs.GetBounds();
                float width = iconText.Length == 1 ? 22 : 30;
                float height = 28;
                float scaleX = width / bounds.Width;
                float scaleY = height / bounds.Height;
                using (var transform = new Matrix(scaleX, 0, 0, scaleY, (32 - width) / 2 - bounds.X * scaleX, (32 - height) / 2 - bounds.Y * scaleY))
                {
                    glyphs.Transform(transform);
                    using (var textBrush = new SolidBrush(TrayTheme.TextColor())) g.FillPath(textBrush, glyphs);
                }
                IntPtr raw = bitmap.GetHicon();
                Icon next;
                try { using (var borrowed = Icon.FromHandle(raw)) next = (Icon)borrowed.Clone(); }
                finally { DestroyIcon(raw); }
                tray.Icon = next; if (currentIcon != null) currentIcon.Dispose(); currentIcon = next;
            }
            string period = preferWeekly ? L.T("Неделя","Week") : L.T("5 часов","5 hours");
            string tooltip = period + (valid ? L.T(": осталось ",": remaining ") + remaining.ToString("0.#") + "%" : L.T(": нет актуальных данных",": no current data"));
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            widget.UpdateQuota(Quota.FiveHour(quotas), Quota.Weekly(quotas), error, updated, preferWeekly);
        }
        protected override void ExitThreadCore()
        {
            closing = true; SystemEvents.UserPreferenceChanged -= ThemeChanged; LimitsClient.Stop(); timer.Stop(); timer.Dispose();
            tray.Visible = false; tray.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            widget.Dispose(); menu.Dispose();
            base.ExitThreadCore();
        }
    }
}
