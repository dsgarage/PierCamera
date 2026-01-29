using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// MCP 経由でテストを実行するヘルパー（Assembly-CSharp-Editor に配置）。
/// unity.editor.invokeStaticMethod から呼び出す。
/// </summary>
public static class MCPTestRunner
{
    public static string RunTestAssembly(string assemblyName)
    {
        var results = new List<string>();
        int passed = 0;
        int failed = 0;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        if (assembly == null)
        {
            var available = string.Join(", ", AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => n.Contains("Test") || n.Contains("AICam"))
                .OrderBy(n => n));
            return $"Assembly '{assemblyName}' not found.\nAvailable: {available}";
        }

        var testAttrType = Type.GetType("NUnit.Framework.TestAttribute, nunit.framework");
        var testCaseAttrType = Type.GetType("NUnit.Framework.TestCaseAttribute, nunit.framework");
        var fixtureAttrType = Type.GetType("NUnit.Framework.TestFixtureAttribute, nunit.framework");
        var setupAttrType = Type.GetType("NUnit.Framework.OneTimeSetUpAttribute, nunit.framework");

        if (testAttrType == null || fixtureAttrType == null)
            return "NUnit framework not found.";

        var testFixtures = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute(fixtureAttrType) != null)
            .OrderBy(t => t.Name)
            .ToArray();

        foreach (var fixture in testFixtures)
        {
            results.Add($"\n--- {fixture.Name} ---");
            object instance = null;

            try
            {
                instance = Activator.CreateInstance(fixture);
                var setupMethod = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance)
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

            // [Test] methods
            var testMethods = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute(testAttrType) != null
                            && !m.GetCustomAttributes(testCaseAttrType).Any())
                .OrderBy(m => m.Name)
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
            var testCaseMethods = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes(testCaseAttrType).Any())
                .OrderBy(m => m.Name)
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

        var status = failed == 0 ? "ALL PASSED" : "FAILURES DETECTED";
        results.Insert(0, $"[{status}] {passed} passed, {failed} failed, {passed + failed} total");

        var output = string.Join("\n", results);
        Debug.Log($"[MCPTestRunner] {output}");
        return output;
    }
}
