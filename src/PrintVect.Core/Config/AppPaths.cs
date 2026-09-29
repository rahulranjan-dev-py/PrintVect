using System;
using System.IO;

namespace PrintVect.Core.Config
{
    /// <summary>
    /// Where PrintVect keeps its files. Defaults to %ProgramData%\PrintVect so the data is shared by
    /// every user of the PC and survives a user switch (brief, section 3). Tests pass their own root.
    /// </summary>
    public sealed class AppPaths
    {
        public const string ProductFolderName = "PrintVect";
        public const string ConfigFileName = "config.json";
        public const string SpoolFolderName = "spool";
        public const string LogsFolderName = "logs";

        public AppPaths(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("A root folder is required.", nameof(root));
            }
            Root = Path.GetFullPath(root);
        }

        public string Root { get; }
        public string ConfigFile { get { return Path.Combine(Root, ConfigFileName); } }
        public string SpoolDir { get { return Path.Combine(Root, SpoolFolderName); } }
        public string LogsDir { get { return Path.Combine(Root, LogsFolderName); } }

        /// <summary>%ProgramData%\PrintVect (C:\ProgramData\PrintVect on a normal install).</summary>
        public static AppPaths Default()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return new AppPaths(Path.Combine(programData, ProductFolderName));
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(SpoolDir);
            Directory.CreateDirectory(LogsDir);
        }
    }
}
