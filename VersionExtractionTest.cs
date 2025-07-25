using System;
using System.Text.RegularExpressions;

namespace EdgeMonitor.Tests
{
    public static class VersionExtractionTest
    {
        public static void TestVersionExtraction()
        {
            var testCases = new[]
            {
                ("EdgeMonitor-v1.3", "v1.3"),
                ("EdgeMonitor-v1.3.0", "v1.3.0"),
                ("EM-v1.4", "v1.4"),
                ("EM-v1.4.2", "v1.4.2"),
                ("Edge Monitor-v2.0", "v2.0"),
                ("MyApp-v3.1.5", "v3.1.5"),
                ("v1.5", "v1.5"),
                ("v2.0.1", "v2.0.1"),
                ("1.6", "v1.6"),
                ("2.1.0", "v2.1.0"),
                ("Release", null), // 应该返回null
                ("", null), // 应该返回null
            };

            Console.WriteLine("版本号提取测试:");
            Console.WriteLine("================");

            foreach (var (input, expected) in testCases)
            {
                var result = ExtractVersionFromReleaseName(input);
                var status = result == expected ? "✅ PASS" : "❌ FAIL";
                Console.WriteLine($"{status} \"{input}\" → \"{result}\" (期望: \"{expected}\")");
            }
        }

        private static string? ExtractVersionFromReleaseName(string? releaseName)
        {
            if (string.IsNullOrEmpty(releaseName)) return null;

            try
            {
                // 尝试匹配各种可能的版本号格式
                var patterns = new[]
                {
                    @"EdgeMonitor-v(\d+\.\d+(?:\.\d+)?)",     // EdgeMonitor-v1.3 或 EdgeMonitor-v1.3.0
                    @"EM-v(\d+\.\d+(?:\.\d+)?)",             // EM-v1.4 或 EM-v1.4.0
                    @"Edge\s*Monitor-v(\d+\.\d+(?:\.\d+)?)", // Edge Monitor-v1.3 (带空格)
                    @"[A-Za-z\s]*-v(\d+\.\d+(?:\.\d+)?)",    // 任意前缀-v1.3 (通用匹配)
                    @"v(\d+\.\d+(?:\.\d+)?)",                // v1.3 或 v1.3.0
                    @"(\d+\.\d+(?:\.\d+)?)"                  // 1.3 或 1.3.0 (纯数字)
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(releaseName, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return "v" + match.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // 解析失败时返回null
            }

            return null;
        }
    }
}
