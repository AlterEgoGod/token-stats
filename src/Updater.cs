using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TokenStats
{
    sealed class ReleaseInfo
    {
        public Version Version;
        public string DownloadUrl;
        public string Sha256;
    }

    static class Updater
    {
        const string ApiUrl = "https://api.github.com/repos/AlterEgoGod/token-stats/releases/latest";
        const string DownloadPrefix = "https://github.com/AlterEgoGod/token-stats/releases/download/";
        public static Version CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        public static Version ParseVersion(string tag)
        {
            Version value;
            if (String.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out value))
                throw new InvalidDataException(L.T("Некорректная версия релиза GitHub.","Invalid GitHub release version."));
            return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        }

        public static ReleaseInfo ParseRelease(string json)
        {
            var release = Json.Parse(json);
            if (Object.Equals(Json.Get(release, "draft"), true) || Object.Equals(Json.Get(release, "prerelease"), true)) return null;
            var version = ParseVersion(Json.Str(release, "tag_name"));
            if (version <= CurrentVersion) return new ReleaseInfo { Version = version };
            var assets = Json.Get(release, "assets") as System.Collections.IEnumerable;
            if (assets != null) foreach (var item in assets)
            {
                var asset = item as System.Collections.Generic.Dictionary<string, object>;
                if (Json.Str(asset, "name") != "TokenStats.exe") continue;
                string url = Json.Str(asset, "browser_download_url");
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !String.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidDataException(L.T("Файл обновления должен находиться в релизе AlterEgoGod/token-stats.","Update must be hosted in AlterEgoGod/token-stats releases."));
                string digest = Json.Str(asset, "digest");
                if (!System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
                    throw new InvalidDataException(L.T("GitHub ещё не предоставил контрольную сумму обновления. Попробуйте позже.","GitHub has not provided the update checksum. Try again later."));
                return new ReleaseInfo { Version = version, DownloadUrl = url, Sha256 = digest.Substring(7) };
            }
            throw new InvalidDataException(L.T("В новом релизе нет файла TokenStats.exe.","Release does not contain TokenStats.exe."));
        }

        static byte[] ReadUrl(string url, int maxBytes)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "TokenStats/" + CurrentVersion;
            request.Accept = url == ApiUrl ? "application/vnd.github+json" : "application/octet-stream";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException(L.T("Обновление требует HTTPS.","Updates require HTTPS."));
                var buffer = new byte[8192];
                var elapsed = Stopwatch.StartNew();
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > maxBytes) throw new InvalidDataException(L.T("Файл обновления слишком большой.","Update file is too large."));
                    if (elapsed.Elapsed.TotalMinutes > 2) throw new TimeoutException(L.T("Превышено время загрузки обновления.","Update download timed out."));
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }

        public static Task<ReleaseInfo> Check()
        {
            return Task.Run(() => {
                try { return ParseRelease(System.Text.Encoding.UTF8.GetString(ReadUrl(ApiUrl, 1024 * 1024))); }
                catch (WebException e)
                {
                    using (var response = e.Response as HttpWebResponse)
                    {
                        if (response != null && response.StatusCode == HttpStatusCode.NotFound) return null;
                        if (response != null && (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429))
                            throw new IOException(L.T("GitHub временно ограничил запросы. Попробуйте позже.","GitHub rate limit reached. Try again later."), e);
                    }
                    throw new IOException(L.T("Не удалось связаться с GitHub. Проверьте подключение к интернету.","Cannot reach GitHub. Check your connection."), e);
                }
            });
        }

        public static void VerifyHash(byte[] data, string expected)
        {
            using (var hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
                if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L.T("Контрольная сумма обновления не совпала. Установка отменена.","Update checksum mismatch. Installation cancelled."));
            }
        }

        public static Task<string> Download(ReleaseInfo release)
        {
            return Task.Run(() => {
                byte[] data = ReadUrl(release.DownloadUrl, 32 * 1024 * 1024);
                VerifyHash(data, release.Sha256);
                string folder = Path.Combine(Path.GetTempPath(), "TokenStats-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "TokenStats.exe");
                try
                {
                    File.WriteAllBytes(path, data);
                    var assembly = AssemblyName.GetAssemblyName(path);
                    if (assembly.Name != "TokenStats" || assembly.Version != release.Version)
                        throw new InvalidDataException(L.T("Версия скачанного приложения не совпадает с версией релиза.","Downloaded app version does not match the release."));
                    return path;
                }
                catch { File.Delete(path); Directory.Delete(folder); throw; }
            });
        }

        public static void StartInstall(string downloaded)
        {
            // Check destination access before asking the running application to exit.
            string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".tokenstats-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            var process = Process.Start(new ProcessStartInfo(downloaded,
                "--apply-update " + Process.GetCurrentProcess().Id + " \"" + Application.ExecutablePath + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (process == null) throw new IOException(L.T("Не удалось запустить установку обновления.","Could not start update installation."));
            process.Dispose();
        }

        public static void ReplaceExecutable(string source, string target, string backup)
        {
            string next = target + "." + Guid.NewGuid().ToString("N") + ".new";
            try { File.Copy(source, next); File.Replace(next, target, backup); }
            finally { if (File.Exists(next)) File.Delete(next); }
        }

        public static int Apply(string[] args)
        {
            string backup = null;
            string target = null;
            bool replaced = false;
            bool parentExited = false;
            try
            {
                int parentId;
                if (args.Length != 3 || !Int32.TryParse(args[1], out parentId)) throw new ArgumentException(L.T("Некорректные параметры обновления.","Invalid update arguments."));
                target = Path.GetFullPath(args[2]);
                if (!String.Equals(Path.GetFileName(target), "TokenStats.exe", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(target, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                    throw new ArgumentException(L.T("Некорректный путь установки TokenStats.","Invalid TokenStats installation path."));
                Process parent = null;
                try { parent = Process.GetProcessById(parentId); } catch (ArgumentException) { }
                if (parent != null) using (parent) { if (!parent.WaitForExit(30000)) throw new IOException(L.T("TokenStats не завершился. Повторите обновление.","TokenStats did not close. Retry the update.")); }
                parentExited = true;
                backup = target + "." + Guid.NewGuid().ToString("N") + ".bak";
                ReplaceExecutable(Application.ExecutablePath, target, backup);
                replaced = true;
                using (var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }))
                {
                    if (process == null || process.WaitForExit(3000)) throw new IOException(L.T("Обновлённое приложение не запустилось.","Updated application did not start."));
                }
                try { File.Delete(backup); } catch (IOException) { }
                return 0;
            }
            catch (Exception e)
            {
                string message = L.T("Не удалось установить обновление: ","Could not install update: ") + e.Message;
                if (replaced && File.Exists(backup))
                {
                    try { File.Copy(backup, target, true); Process.Start(new ProcessStartInfo(target) { UseShellExecute = false }); }
                    catch (Exception restoreError) { message += L.T("\nРезервная копия: ","\nBackup: ") + backup + "\n" + restoreError.Message; }
                }
                if (parentExited && !replaced)
                {
                    try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = false }); }
                    catch (Exception restartError) { message += L.T("\nНе удалось запустить прежнюю версию: ","\nCould not start the previous version: ") + restartError.Message; }
                }
                MessageBox.Show(message, L.T("Обновление TokenStats","TokenStats update"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
