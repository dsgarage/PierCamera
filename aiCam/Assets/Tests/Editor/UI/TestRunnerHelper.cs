using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AICam.UI.Tests
{
    /// <summary>
    /// MCP 経由でテストを実行するためのヘルパー。
    /// unity.editor.invokeStaticMethod から呼び出す。
    /// </summary>
    public static class TestRunnerHelper
    {
        public static string RunAll()
        {
            var results = new List<string>();
            int passed = 0;
            int failed = 0;

            var assembly = Assembly.GetExecutingAssembly();
            var testFixtures = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute(
                    Type.GetType("NUnit.Framework.TestFixtureAttribute, nunit.framework")) != null)
                .ToArray();

            var testAttrType = Type.GetType("NUnit.Framework.TestAttribute, nunit.framework");
            var testCaseAttrType = Type.GetType("NUnit.Framework.TestCaseAttribute, nunit.framework");
            var setupAttrType = Type.GetType("NUnit.Framework.OneTimeSetUpAttribute, nunit.framework");

            foreach (var fixture in testFixtures)
            {
                results.Add($"\n=== {fixture.Name} ===");
                object instance = null;

                try
                {
                    instance = Activator.CreateInstance(fixture);

                    // Run OneTimeSetUp
                    var setupMethod = fixture.GetMethods()
                        .FirstOrDefault(m => m.GetCustomAttribute(setupAttrType) != null);
                    setupMethod?.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    results.Add($"  [FAIL] OneTimeSetUp: {inner.Message}");
                    failed++;
                    continue;
                }

                // Regular [Test] methods
                var testMethods = fixture.GetMethods()
                    .Where(m => m.GetCustomAttribute(testAttrType) != null
                                && m.GetCustomAttribute(testCaseAttrType) == null)
                    .ToArray();

                foreach (var method in testMethods)
                {
                    try
                    {
                        method.Invoke(instance, null);
                        results.Add($"  [PASS] {method.Name}");
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        results.Add($"  [FAIL] {method.Name}: {inner.Message}");
                        failed++;
                    }
                }

                // [TestCase] methods
                var testCaseMethods = fixture.GetMethods()
                    .Where(m => m.GetCustomAttributes(testCaseAttrType).Any())
                    .ToArray();

                foreach (var method in testCaseMethods)
                {
                    var cases = method.GetCustomAttributes(testCaseAttrType);
                    foreach (var tc in cases)
                    {
                        var argsProp = testCaseAttrType.GetProperty("Arguments");
                        var args = (object[])argsProp.GetValue(tc);
                        var label = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));

                        try
                        {
                            method.Invoke(instance, args);
                            results.Add($"  [PASS] {method.Name}({label})");
                            passed++;
                        }
                        catch (Exception ex)
                        {
                            var inner = ex.InnerException ?? ex;
                            results.Add($"  [FAIL] {method.Name}({label}): {inner.Message}");
                            failed++;
                        }
                    }
                }
            }

            var summary = $"\n{'='} RESULTS: {passed} passed, {failed} failed, {passed + failed} total {'='}";
            results.Insert(0, summary);

            var output = string.Join("\n", results);
            Debug.Log(output);
            return output;
        }
    }
}
