using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PrintVect.Core.Config;

namespace PrintVect.Core.Elevation
{
    /// <summary>A folder PrintVect works in turned out to be a junction or symbolic link (0.5.8, SafeFolders).</summary>
    public sealed class LinkFoundException : IOException
    {
        public LinkFoundException(string path)
            : base("PrintVect did not touch " + path + ": it is a link to another place (a junction or symbolic link), which PrintVect never "
                   + "makes. Another user of this PC may have put it there to misuse the administrator rights of PrintVect.Elevate. "
                   + "Delete " + path + " (the link only, not where it leads), then try again.")
        {
            Path = path;
        }

        public string Path { get; }
    }

    /// <summary>
    /// Every user may write in %ProgramData%\PrintVect, so any of them could replace a folder there with a junction to a
    /// system folder before an administrator command runs (0.5.8). PrintVect.Elevate therefore creates, opens, grants
    /// rights on and deletes folders there only after making sure that no folder on the way is a link, and grants the
    /// spool rights through a handle to the very folder it checked (so it cannot be swapped in between).
    /// </summary>
    public static class SafeFolders
    {
        /// <summary>The folder that holds the data folder (%ProgramData%): links are looked for below it.</summary>
        public static string TopOf(AppPaths paths)
        {
            return System.IO.Path.GetDirectoryName(Full(paths.Root));
        }

        /// <summary>The first link among the data, spool and logs folders, or null: what every administrator command needs.</summary>
        public static string FirstLinkInDataFolders(AppPaths paths)
        {
            string top = TopOf(paths);
            return FirstLink(top, paths.SpoolDir) ?? FirstLink(top, paths.LogsDir);
        }

        /// <summary>
        /// The first existing folder or file from the child of <paramref name="top"/> down to <paramref name="path"/> that
        /// is a junction or symbolic link, or null. <paramref name="path"/> must lie below <paramref name="top"/>.
        /// </summary>
        public static string FirstLink(string top, string path)
        {
            string root = Full(top);
            string full = Full(path);
            if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException(path + " is not inside " + top + ".", nameof(path));
            }
            string current = root;
            foreach (string part in full.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);   // of the entry itself: a link is not followed at the last step
                }
                catch (FileNotFoundException)
                {
                    return null;   // nothing deeper exists either
                }
                catch (DirectoryNotFoundException)
                {
                    return null;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return current;
                }
            }
            return null;
        }

        /// <summary>Throws LinkFoundException when FirstLink finds one.</summary>
        public static void RefuseLinks(string top, string path)
        {
            string link = FirstLink(top, path);
            if (link != null)
            {
                throw new LinkFoundException(link);
            }
        }

        /// <summary>Creates <paramref name="folder"/> (and the folders on the way) below <paramref name="top"/>, refusing links.</summary>
        public static void CreateFolder(string top, string folder)
        {
            RefuseLinks(top, folder);
            Directory.CreateDirectory(folder);
            RefuseLinks(top, folder);
        }

        /// <summary>
        /// Lets every user of the PC rename and delete the files the spooler writes into <paramref name="folder"/>
        /// (ProgramData gives Users only create rights; files there belong to whoever created them). The folder is
        /// opened without following a link, checked to be the real folder at that path, and changed through that handle.
        /// </summary>
        public static void GrantUsersModify(string top, string folder)
        {
            CreateFolder(top, folder);
            using (SafeFileHandle handle = Open(folder, ReadControl | WriteDac | FileReadAttributes, true))
            {
                ByHandleFileInformation info;
                if (!GetFileInformationByHandle(handle, out info))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The folder " + folder + " could not be checked");
                }
                if ((info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.FileAttributes & (uint)FileAttributes.Directory) == 0)
                {
                    throw new LinkFoundException(folder);
                }
                string expected;
                using (SafeFileHandle topHandle = Open(top, FileReadAttributes, false))
                {
                    expected = FinalPath(topHandle) + Full(folder).Substring(Full(top).Length);
                }
                string actual = FinalPath(handle);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new LinkFoundException(folder + " (it leads to " + actual + ")");
                }

                var security = new HandleSecurity(handle);
                security.Add(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Modify | FileSystemRights.Synchronize,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                security.Save(handle);
            }
        }

        private static string Full(string path)
        {
            return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
        }

        private static SafeFileHandle Open(string path, uint access, bool noFollow)
        {
            SafeFileHandle handle = CreateFile(path, access, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
                FileFlagBackupSemantics | (noFollow ? FileFlagOpenReparsePoint : 0), IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "The folder " + path + " could not be opened");
            }
            return handle;
        }

        /// <summary>The path Windows itself gives the opened folder, without the \\?\ prefix.</summary>
        private static string FinalPath(SafeFileHandle handle)
        {
            var buffer = new StringBuilder(1024);
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The real path of a folder could not be read");
            }
            string path = buffer.ToString();
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8).TrimEnd('\\');
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4).TrimEnd('\\');
            return path.TrimEnd('\\');
        }

        /// <summary>A folder's access list read and written through an open handle (NativeObjectSecurity does the Windows calls).</summary>
        private sealed class HandleSecurity : NativeObjectSecurity
        {
            public HandleSecurity(SafeHandle handle) : base(true, ResourceType.FileObject, handle, AccessControlSections.Access)
            {
            }

            public void Add(FileSystemAccessRule rule)
            {
                AddAccessRule(rule);
            }

            public void Save(SafeHandle handle)
            {
                Persist(handle, AccessControlSections.Access);
            }

            public override Type AccessRightType
            {
                get { return typeof(FileSystemRights); }
            }

            public override Type AccessRuleType
            {
                get { return typeof(FileSystemAccessRule); }
            }

            public override Type AuditRuleType
            {
                get { return typeof(FileSystemAuditRule); }
            }

            public override AccessRule AccessRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited,
                InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AccessControlType type)
            {
                return new FileSystemAccessRule(identityReference, (FileSystemRights)accessMask, inheritanceFlags, propagationFlags, type);
            }

            public override AuditRule AuditRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited,
                InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AuditFlags flags)
            {
                return new FileSystemAuditRule(identityReference, (FileSystemRights)accessMask, inheritanceFlags, propagationFlags, flags);
            }
        }

        private const uint ReadControl = 0x00020000;
        private const uint WriteDac = 0x00040000;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
            FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    }
}
