using System;
using System.Reflection;

namespace PrintVect.Core
{
    /// <summary>Product name and version, shared by the app, the Elevate helper and the logs.</summary>
    public static class AppInfo
    {
        public const string ProductName = "PrintVect";

        /// <summary>Three-part version such as "0.1.0" (from Directory.Build.props).</summary>
        public static string Version
        {
            get
            {
                Version v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "0.0.0" : v.ToString(3);
            }
        }
    }
}
