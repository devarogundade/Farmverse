#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Farmverse.TestHost
{
    /// <summary>Discovers and runs [TestFixture] classes in this assembly. Exit code = failures.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Unity runs tests with the project root as the working directory; match it.
            var root = FindRepositoryRoot();
            Directory.SetCurrentDirectory(root);

            var filter = args.Length > 0 ? args[0] : null;
            var passed = 0;
            var failed = 0;
            var fixtures = typeof(Program).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestFixtureAttribute>() != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

            foreach (var fixture in fixtures)
            {
                var methods = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                var setUp = methods.FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
                var tearDown = methods.FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);

                foreach (var method in methods.OrderBy(m => m.MetadataToken))
                {
                    var cases = method.GetCustomAttributes<TestCaseAttribute>().Select(c => c.Arguments).ToList();
                    if (method.GetCustomAttribute<TestAttribute>() != null && cases.Count == 0)
                    {
                        cases.Add(Array.Empty<object?>());
                    }
                    foreach (var arguments in cases)
                    {
                        var name = $"{fixture.Name}.{method.Name}" + (arguments.Length > 0 ? $"({string.Join(", ", arguments)})" : "");
                        if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                        var instance = Activator.CreateInstance(fixture)!;
                        try
                        {
                            setUp?.Invoke(instance, null);
                            method.Invoke(instance, arguments);
                            passed++;
                        }
                        catch (TargetInvocationException error)
                        {
                            failed++;
                            Console.WriteLine($"FAIL {name}\n  {error.InnerException?.GetType().Name}: {error.InnerException?.Message}");
                            if (error.InnerException is not AssertionException) Console.WriteLine(error.InnerException?.StackTrace);
                        }
                        finally
                        {
                            try { tearDown?.Invoke(instance, null); } catch (TargetInvocationException error) { failed++; Console.WriteLine($"FAIL {name} (teardown) {error.InnerException?.Message}"); }
                        }
                    }
                }
            }

            Console.WriteLine($"\n{passed} passed, {failed} failed");
            return failed;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
