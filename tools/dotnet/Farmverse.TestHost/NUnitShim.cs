#nullable enable
// A minimal stand-in for the parts of NUnit 3 the EditMode tests use. Unity compiles
// the same tests against the real NUnit; keep this API-compatible with it.
using System;
using System.Collections;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute
    {
        public TestCaseAttribute(params object?[] arguments) { Arguments = arguments; }
        public object?[] Arguments { get; }
    }

    public delegate void TestDelegate();

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void AreEqual(object? expected, object? actual, string? message = null)
        {
            if (!AreEqualValues(expected, actual))
                Fail($"Expected: {Show(expected)}\n  But was: {Show(actual)}" + Suffix(message));
        }

        public static void AreNotEqual(object? expected, object? actual, string? message = null)
        {
            if (AreEqualValues(expected, actual)) Fail($"Expected not: {Show(expected)}" + Suffix(message));
        }

        public static void AreSame(object? expected, object? actual, string? message = null)
        {
            if (!ReferenceEquals(expected, actual)) Fail("Expected the same instance" + Suffix(message));
        }

        public static void IsTrue(bool condition, string? message = null) { if (!condition) Fail("Expected: True" + Suffix(message)); }
        public static void IsFalse(bool condition, string? message = null) { if (condition) Fail("Expected: False" + Suffix(message)); }
        public static void IsNull(object? value, string? message = null) { if (value != null) Fail($"Expected: null but was {Show(value)}" + Suffix(message)); }
        public static void IsNotNull(object? value, string? message = null) { if (value == null) Fail("Expected: not null" + Suffix(message)); }

        public static T Throws<T>(TestDelegate code, string? message = null) where T : Exception
        {
            try { code(); }
            catch (T expected) when (expected.GetType() == typeof(T)) { return expected; }
            catch (Exception other) { Fail($"Expected {typeof(T).Name} but got {other.GetType().Name}: {other.Message}" + Suffix(message)); }
            Fail($"Expected {typeof(T).Name} but nothing was thrown" + Suffix(message));
            return null!;
        }

        public static void AreEqual(double expected, double actual, double delta, string? message = null)
        {
            if (Math.Abs(expected - actual) > delta) Fail($"Expected: {expected} +/- {delta}\n  But was: {actual}" + Suffix(message));
        }

        public static void IsInstanceOf<T>(object? actual, string? message = null)
        {
            if (!(actual is T)) Fail($"Expected an instance of {typeof(T).Name} but was {Show(actual)}" + Suffix(message));
        }

        public static void LessOrEqual(long arg1, long arg2, string? message = null)
        {
            if (arg1 > arg2) Fail($"Expected: less than or equal to {arg2}\n  But was: {arg1}" + Suffix(message));
        }

        public static void Fail(string message) => throw new AssertionException(message);

        private static bool AreEqualValues(object? expected, object? actual)
        {
            if (expected == null || actual == null) return expected == null && actual == null;
            if (IsNumber(expected) && IsNumber(actual))
                return Convert.ToDecimal(expected) == Convert.ToDecimal(actual);
            return expected.Equals(actual);
        }

        private static bool IsNumber(object value) =>
            value is int || value is long || value is short || value is byte || value is double || value is float || value is decimal;

        private static string Show(object? value) => value == null ? "null" : value is string s ? $"\"{s}\"" : value.ToString() ?? "";
        private static string Suffix(string? message) => message == null ? "" : "\n  " + message;
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string? actual, string? message = null)
        {
            if (actual == null || !actual.Contains(expected)) Assert.Fail($"Expected a string containing \"{expected}\" but was \"{actual}\"" + (message == null ? "" : "\n  " + message));
        }

        public static void StartsWith(string expected, string? actual, string? message = null)
        {
            if (actual == null || !actual.StartsWith(expected, StringComparison.Ordinal)) Assert.Fail($"Expected a string starting with \"{expected}\" but was \"{actual}\"" + (message == null ? "" : "\n  " + message));
        }
    }

    public static class CollectionAssert
    {
        public static void Contains(IEnumerable collection, object? actual, string? message = null)
        {
            foreach (var item in collection) if (Equals(item, actual)) return;
            Assert.Fail($"Expected the collection to contain {actual}" + (message == null ? "" : "\n  " + message));
        }

        public static void DoesNotContain(IEnumerable collection, object? actual, string? message = null)
        {
            foreach (var item in collection) if (Equals(item, actual)) Assert.Fail($"Expected the collection not to contain {actual}" + (message == null ? "" : "\n  " + message));
        }

        public static void AreEqual(IEnumerable expected, IEnumerable actual, string? message = null)
        {
            var e = expected.GetEnumerator();
            var a = actual.GetEnumerator();
            var index = 0;
            while (true)
            {
                var hasE = e.MoveNext();
                var hasA = a.MoveNext();
                if (!hasE && !hasA) return;
                if (hasE != hasA) Assert.Fail($"Collections differ in length at index {index}" + (message == null ? "" : "\n  " + message));
                Assert.AreEqual(e.Current, a.Current, $"at index {index}" + (message == null ? "" : ": " + message));
                index++;
            }
        }
    }
}
