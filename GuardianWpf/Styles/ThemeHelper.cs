using System;
using System.Windows;
using System.Windows.Media;

namespace GuardianWpf.Styles
{
    public static class ThemeHelper
    {
        public static Brush GetBrush(string key, Brush? fallback = null)
        {
            try
            {
                if (Application.Current != null && Application.Current.Resources.Contains(key))
                {
                    if (Application.Current.Resources[key] is Brush b)
                    {
                        return b;
                    }
                }
            }
            catch { }

            return fallback ?? Brushes.DodgerBlue;
        }

        public static Style? GetStyle(string key)
        {
            try
            {
                if (Application.Current != null && Application.Current.Resources.Contains(key))
                {
                    return Application.Current.Resources[key] as Style;
                }
            }
            catch { }

            return null;
        }
    }
}
