using System;
namespace TokenStats
{
    static class Tests
    {
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
        public static int Run()
        {
            try
            {

                UpdateTests.Run();
                WidgetTests.Run();
                var rows = Quota.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":2000000000},\"secondary\":{\"usedPercent\":90,\"windowDurationMins\":10080}}}"));
                Check(rows.Count == 2 && rows[0].Remaining == 75 && rows[1].Remaining == 10, "remaining");
                Check(rows[0].Window == L.T("5 ч.","5 h") && rows[1].Window == L.T("Неделя","Week"), "windows");
                Check(!rows[1].Reset.HasValue, "missing reset");
                Check(Quota.Weekly(rows) == rows[1], "weekly quota selection");
                Check(Quota.Weekly(new[] { rows[0] }) == null, "missing weekly quota remains unknown");
                Check(Quota.FiveHour(rows) == rows[0], "five-hour quota, not lower weekly quota");
                Check(Quota.FiveHour(new[] { rows[1] }) == null, "no five-hour fallback to weekly");
                var premium = Json.Parse("{\"rateLimits\":{\"planType\":\"self_serve_business_prolite\",\"primary\":{\"usedPercent\":37,\"windowDurationMins\":10080},\"secondary\":null}}");
                Check(Quota.PreferWeekly(premium), "Business Premium plan detection");
                Check(Quota.Primary(Quota.Parse(premium), Quota.PreferWeekly(premium)).Remaining == 63, "weekly primary window without five-hour quota");
                Check(Quota.Primary(rows, true) == rows[1], "Premium selects weekly even when both windows exist");
                Check(Quota.Primary(rows, false) == rows[0], "other plans keep five-hour quota");
                Check(Quota.Primary(new[] { rows[0] }, true) == null, "missing Premium weekly quota never falls back to five hours");
                foreach (string plan in new[] { "plus", "pro", "team", "business", "self_serve_business_usage_based", "unknown", "" })
                    Check(!Quota.PreferWeekly(Json.Parse("{\"rateLimits\":{\"planType\":\"" + plan + "\"}}")), "other plan unchanged: " + plan);
                Check(!Quota.PreferWeekly(Json.Parse("{}")), "missing plan keeps default");
                Check(Quota.PreferWeekly(Json.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"planType\":\"self_serve_business_prolite\"},\"spark\":{\"planType\":\"plus\"}}}")), "Codex bucket plan detection");
                Check(!Quota.PreferWeekly(Json.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"planType\":\"plus\"},\"spark\":{\"planType\":\"self_serve_business_prolite\"}}}")), "ignore other bucket plan");
                Check(!Quota.PreferWeekly(Json.Parse("{\"rateLimits\":{\"planType\":\"self_serve_business_prolite\"},\"rateLimitsByLimitId\":{\"codex\":{\"planType\":\"plus\"}}}")), "Codex bucket overrides legacy plan");
                Check(Quota.PreferWeekly(Json.Parse("{\"rateLimits\":{\"planType\":\"self_serve_business_prolite\"},\"rateLimitsByLimitId\":{\"codex\":{\"planType\":null}}}")), "legacy plan fallback");
                var spark = new Quota { BucketId = "spark", WindowMinutes = 300, Remaining = 1 };
                Check(Quota.FiveHour(new[] { spark, rows[0] }) == rows[0], "ignore other five-hour buckets");
                Check(Quota.Primary(new[] { new Quota { BucketId = "spark", WindowMinutes = 10080, Remaining = 1 }, rows[1] }, true) == rows[1], "ignore other weekly buckets");
                rows = Quota.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":50}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":100,\"resetsAt\":1}},\"spark\":{\"primary\":{\"usedPercent\":0.5}}}}"));
                Check(rows.Count == 2 && rows[0].Remaining == 0 && rows[0].Expired && rows[1].Remaining == 99.5, "multi bucket and expired");
                Check(Quota.Parse(Json.Parse("{\"rateLimits\":{\"primary\":null}}")).Count == 0, "unknown isn't 100 percent");
                Check(Quota.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{}}}")).Count == 0, "missing used percent");
                bool rejected = false;
                try { Quota.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":-1}}}")); } catch { rejected = true; }
                Check(rejected, "invalid percentage");
                Console.WriteLine("PASS: quota parsing, windows, missing data, expiry, invalid values, multiple buckets.");
                return 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
        }
    }
}
