#if NETSTANDARD2_0
namespace System
{
    internal static class StringExtensions
    {
        public static bool Contains(this string str, char chr, StringComparison comparison)
        {
            return str.IndexOf(chr.ToString(), comparison) != -1;
        }

        public static bool Contains(this string str, string value, StringComparison comparison)
        {
            return str.IndexOf(value, comparison) != -1;
        }

        public static string Replace(this string str, string oldValue, string newValue, StringComparison comparison)
        {
            return str.Replace(oldValue, newValue);
        }
    }
}
#endif
