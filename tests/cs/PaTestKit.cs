// Tiny test runner for the UnityEngine-free PortAudio core (tests/test_port_audio_cs.py builds
// one exe per area from Core/... + this file + tests/cs/<Area>/**).
//
// A test is a method marked [PaTest]: static, or an instance method of a class with a
// parameterless constructor; no parameters. Main runs every test (or those whose "Class.Method"
// name contains one of the command line arguments), prints PASS/FAIL lines and returns the
// number of failures as the exit code.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PortAudio.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PaTestAttribute : Attribute
    {
    }

    public sealed class PaAssertException : Exception
    {
        public PaAssertException(string message) : base(message)
        {
        }
    }

    public static class PaAssert
    {
        public static void Fail(string message)
        {
            throw new PaAssertException(message);
        }

        public static void True(bool condition, string message = null)
        {
            if (!condition)
                Fail(message ?? "expected true");
        }

        public static void False(bool condition, string message = null)
        {
            if (condition)
                Fail(message ?? "expected false");
        }

        public static void Null(object value, string message = null)
        {
            if (value != null)
                Fail((message != null ? message + ": " : "") + "expected null, got " + Show(value));
        }

        public static void NotNull(object value, string message = null)
        {
            if (value == null)
                Fail(message ?? "expected a value, got null");
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Fail((message != null ? message + ": " : "") + "expected " + Show(expected) + ", got " + Show(actual));
        }

        public static void NotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                Fail((message != null ? message + ": " : "") + "did not expect " + Show(actual));
        }

        public static void Near(double expected, double actual, double tolerance = 1e-6, string message = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                Fail((message != null ? message + ": " : "") + "expected " + Show(expected) + " +- " + Show(tolerance) + ", got " + Show(actual));
        }

        public static void Same(object expected, object actual, string message = null)
        {
            if (!ReferenceEquals(expected, actual))
                Fail((message != null ? message + ": " : "") + "expected the same instance");
        }

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            var e = new List<T>(expected ?? new T[0]);
            var a = actual == null ? null : new List<T>(actual);
            bool same = a != null && a.Count == e.Count;
            for (int i = 0; same && i < e.Count; i++)
                same = EqualityComparer<T>.Default.Equals(e[i], a[i]);
            if (!same)
                Fail((message != null ? message + ": " : "") + "expected " + Show(e) + ", got " + Show(a));
        }

        public static TException Throws<TException>(Action action, string message = null) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                Fail((message != null ? message + ": " : "") + "expected " + typeof(TException).Name + ", got " + ex.GetType().Name + ": " + ex.Message);
            }
            Fail((message != null ? message + ": " : "") + "expected " + typeof(TException).Name + ", nothing was thrown");
            return null;
        }

        public static string Show(object v)
        {
            if (v == null)
                return "null";
            if (v is string)
                return "\"" + v + "\"";
            if (v is double)
                return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is float)
                return ((float)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is IEnumerable && !(v is string))
            {
                var sb = new StringBuilder("[");
                bool first = true;
                foreach (object o in (IEnumerable)v)
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(Show(o));
                }
                return sb.Append("]").ToString();
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    // A scratch directory deleted on Dispose. Write() takes '/' separated relative paths.
    public sealed class PaTempDir : IDisposable
    {
        public readonly string Root;

        public PaTempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pa-test-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root);
        }

        public string PathOf(string relative)
        {
            return System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public string Write(string relative, string text)
        {
            string path = PathOf(relative);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Root, true);
            }
            catch (Exception)
            {
            }
        }
    }

    public static class PaTestMain
    {
        public static int Main(string[] args)
        {
            var tests = new List<MethodInfo>();
            foreach (Type t in typeof(PaTestMain).Assembly.GetTypes())
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.GetCustomAttributes(typeof(PaTestAttribute), false).Length > 0)
                        tests.Add(m);
                }
            }
            tests.Sort((a, b) => string.CompareOrdinal(Name(a), Name(b)));

            int run = 0, failed = 0;
            foreach (MethodInfo m in tests)
            {
                string name = Name(m);
                if (!Selected(name, args))
                    continue;
                run++;
                string error = RunOne(m);
                if (error == null)
                {
                    Console.WriteLine("PASS " + name);
                }
                else
                {
                    failed++;
                    Console.WriteLine("FAIL " + name + ": " + error);
                }
            }
            Console.WriteLine(run + " tests, " + failed + " failed");
            if (run == 0)
            {
                // An area with test files but no [PaTest] method, or a selection that matches nothing.
                Console.WriteLine("FAIL no tests ran: mark each test method with [PaTest] (PortAudio.Tests.PaTestAttribute)");
                return 1;
            }
            return failed;
        }

        static string Name(MethodInfo m)
        {
            return m.DeclaringType.Name + "." + m.Name;
        }

        static bool Selected(string name, string[] args)
        {
            if (args == null || args.Length == 0)
                return true;
            foreach (string a in args)
            {
                if (name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static string RunOne(MethodInfo m)
        {
            try
            {
                if (m.GetParameters().Length != 0)
                    return "a [PaTest] method takes no parameters";
                object target = null;
                if (!m.IsStatic)
                    target = Activator.CreateInstance(m.DeclaringType, true);
                m.Invoke(target, null);
                return null;
            }
            catch (TargetInvocationException tie)
            {
                Exception e = tie.InnerException ?? tie;
                if (e is PaAssertException)
                    return e.Message;
                return e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
        }
    }
}
