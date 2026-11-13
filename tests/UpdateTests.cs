using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace TokenStats
{
    static class UpdateTests
    {
        static void Check(bool value, string name) { if (!value) throw new Exception("Updater: " + name); }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, name);
        }
        public static void Run()
        {
            Check(Updater.ParseVersion("v1.2.0") == new Version(1, 2, 0, 0), "normalize tag version");
            Check(Updater.ParseVersion("v1.10.0") > Updater.ParseVersion("v1.9.0"), "numeric comparison");
            Reject(() => Updater.ParseVersion("v2.0.0-beta"), "reject non-stable version");
            Check(Updater.ParseRelease("{\"tag_name\":\"v0.9.0\"}").Version < Updater.CurrentVersion, "no downgrade");
            Check(Updater.ParseRelease("{\"prerelease\":true}") == null, "skip prerelease");
            Check(Updater.ParseRelease("{\"draft\":true}") == null, "skip draft");
            byte[] payload = new byte[] { 1, 2, 3 };
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
            Updater.VerifyHash(payload, hash);
            Reject(() => Updater.VerifyHash(new byte[] { 1, 2, 4 }, hash), "reject corrupted download");
            var asset = new Dictionary<string, object> {
                { "name", "TokenStats.exe" },
                { "browser_download_url", "https://github.com/AlterEgoGod/token-stats/releases/download/v99.0.0/TokenStats.exe" },
                { "digest", "sha256:" + hash }
            };
            var release = new Dictionary<string, object> { { "tag_name", "v99.0.0" }, { "assets", new[] { asset } } };
            Check(Updater.ParseRelease(Json.Encode(release)).Sha256 == hash, "select executable and digest");
            asset["browser_download_url"] = "https://github.com/another/repo/releases/download/v99.0.0/TokenStats.exe";
            Reject(() => Updater.ParseRelease(Json.Encode(release)), "reject different repository");
            asset["browser_download_url"] = "https://github.com/AlterEgoGod/token-stats/releases/download/v99.0.0/TokenStats.exe";
            asset["digest"] = "";
            Reject(() => Updater.ParseRelease(Json.Encode(release)), "reject missing checksum");
            asset["name"] = "other.exe";
            Reject(() => Updater.ParseRelease(Json.Encode(release)), "reject missing executable");
            string folder = Path.Combine(Path.GetTempPath(), "TokenStats-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "source"), target = Path.Combine(folder, "target"), backup = Path.Combine(folder, "backup");
            try
            {
                File.WriteAllText(source, "new"); File.WriteAllText(target, "old");
                Updater.ReplaceExecutable(source, target, backup);
                Check(File.ReadAllText(target) == "new" && File.ReadAllText(backup) == "old", "atomic replacement preserves backup");
                File.Delete(backup);
                try { Updater.ReplaceExecutable(Path.Combine(folder, "missing"), target, backup); } catch (FileNotFoundException) { }
                Check(File.ReadAllText(target) == "new", "failed replacement preserves executable");
            }
            finally { foreach (var path in Directory.GetFiles(folder)) File.Delete(path); Directory.Delete(folder); }
            Console.WriteLine("PASS: update versions, release assets, hashes, atomic replacement and failure preservation.");
        }
    }
}
