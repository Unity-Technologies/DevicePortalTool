using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DevicePortalTool
{
    public class AppPackageIdentity
    {
        public string PackageName { get; private set; }
        public string AppId { get; private set; }
        public string Publisher { get; private set; }
        public string Version { get; private set; }
        public Version VersionValue { get; private set; }
        public string CpuArchitecture { get; private set; }
        public UInt32 CpuArchitectureValue { get; private set; }
        public string ResourceId { get; private set; }
        public string PackageFullName { get; private set; }
        public string PackageFamilyName { get; private set; }
        public string LaunchId { get; private set; }
        public bool CompleteIdentity { get; private set; }

        public AppPackageIdentity(AppxManifest appxData)
        {
            InitializeInternal(appxData.PackageName, appxData.AppId, appxData.Publisher, appxData.Version, appxData.CpuArchitecture, appxData.ResourceId);
        }

        public AppPackageIdentity(string packageName, string appId, string publisher, string version)
        {
            InitializeInternal(packageName, appId, publisher, version, "neutral", "");
        }

        public AppPackageIdentity(string packageName, string appId, string publisher, string version, string cpuArchitecture, string resourceId)
        {
            InitializeInternal(packageName, appId, publisher, version, cpuArchitecture, resourceId);
        }

        private void InitializeInternal(string packageName, string appId, string publisher, string version, string cpuArchitecture, string resourceId)
        {
            if (String.IsNullOrWhiteSpace(packageName))
                throw new System.ArgumentNullException("PackageName is invalid");
            if (String.IsNullOrWhiteSpace(appId))
                throw new System.ArgumentNullException("AppId is invalid");
            if (String.IsNullOrWhiteSpace(publisher))
                throw new System.ArgumentNullException("Publisher is invalid");
            if (String.IsNullOrWhiteSpace(version))
                throw new System.ArgumentNullException("Version is invalid");

            if (cpuArchitecture == null)
                cpuArchitecture = "neutral";
            if (resourceId == null)
                resourceId = "";

            this.PackageName = packageName;
            this.AppId = appId;
            this.Publisher = publisher;
            this.Version = version;
            this.CpuArchitecture = cpuArchitecture;
            this.ResourceId = resourceId;

            System.Version verObj;
            if (System.Version.TryParse(version, out verObj))
            {
                this.VersionValue = verObj;
            }

            this.CpuArchitectureValue = PackageHelper.ProcessorArchitectureStringToEnum(cpuArchitecture);

            this.PackageFamilyName = PackageHelper.TryGetPackageFamilyName(packageName, publisher);
            this.PackageFullName = PackageHelper.TryGetPackageFullName(packageName, publisher, this.VersionValue, this.CpuArchitectureValue);

            // If PackageFamilyName was successfully retrieved, construct the "LaunchId" (AUMID)
            if (!String.IsNullOrWhiteSpace(this.PackageFamilyName))
            {
                this.LaunchId = this.PackageFamilyName + "!" + this.AppId;
            }
            else this.LaunchId = String.Empty;

            // If we successfully retrieve FamilyName and FullName then we have package identity is "complete"
            // Otherwise one or more properties are missing or invalid
            if (!String.IsNullOrWhiteSpace(this.PackageFullName) && !String.IsNullOrWhiteSpace(this.PackageFamilyName))
            {
                this.CompleteIdentity = true;
            }
        }
    }

    internal class PackageHelper
    {
        public static UInt32 ProcessorArchitectureStringToEnum(string architecture)
        {
            architecture = architecture.ToLowerInvariant();
            switch (architecture)
            {
                case "x86": return (UInt32)APPX_PACKAGE_ARCHITECTURE.APPX_PACKAGE_ARCHITECTURE_X86;
                case "x64": return (UInt32)APPX_PACKAGE_ARCHITECTURE.APPX_PACKAGE_ARCHITECTURE_X64;
                case "arm": return (UInt32)APPX_PACKAGE_ARCHITECTURE.APPX_PACKAGE_ARCHITECTURE_ARM;
                case "arm64": return (UInt32)APPX_PACKAGE_ARCHITECTURE.APPX_PACKAGE_ARCHITECTURE_ARM64;
            }

            return (UInt32)APPX_PACKAGE_ARCHITECTURE.APPX_PACKAGE_ARCHITECTURE_NEUTRAL;
        }

        public static string TryGetPackageFamilyName(string name, string publisherId)
        {
            string packageFamilyName = String.Empty;

            try
            {
                var packageId = new PACKAGE_ID
                {
                    name = name,
                    publisher = publisherId,
                };

                uint packageFamilyNameLength = 0;

                // First get the length of the Package Name -> Pass NULL as Output Buffer
                if (PackageFamilyNameFromId(packageId, ref packageFamilyNameLength, null) == 122) // ERROR_INSUFFICIENT_BUFFER
                {
                    var packageFamilyNameBuilder = new StringBuilder((int)packageFamilyNameLength);
                    if (PackageFamilyNameFromId(packageId, ref packageFamilyNameLength, packageFamilyNameBuilder) == 0)
                    {
                        packageFamilyName = packageFamilyNameBuilder.ToString();
                    }
                }
            }
            catch {; }

            return packageFamilyName;
        }

        public static string TryGetPackageFullName(string name, string publisherId, Version appVersion, UInt32 cpuArchitecture)
        {
            string packageFullName = String.Empty;

            try
            {
                var major = Convert.ToUInt16(appVersion.Major);
                var minor = Convert.ToUInt16(appVersion.Minor);
                var build = Convert.ToUInt16(appVersion.Build);
                var rev = Convert.ToUInt16(appVersion.Revision);

                UInt64 packedVersion =
                    Convert.ToUInt64(rev) << 0 |
                    Convert.ToUInt64(build) << 16 |
                    Convert.ToUInt64(minor) << 32 |
                    Convert.ToUInt64(major) << 48;


                var packageId = new PACKAGE_ID
                {
                    name = name,
                    publisher = publisherId,
                    processorArchitecture = cpuArchitecture,
                    version = packedVersion
                };

                uint packageFullNameLength = 0;

                // First get the length of the Package Name -> Pass NULL as Output Buffer
                if (PackageFullNameFromId(packageId, ref packageFullNameLength, null) == 122) // ERROR_INSUFFICIENT_BUFFER
                {
                    var packageFullNameBuilder = new StringBuilder((int)packageFullNameLength);
                    if (PackageFullNameFromId(packageId, ref packageFullNameLength, packageFullNameBuilder) == 0)
                    {
                        packageFullName = packageFullNameBuilder.ToString();
                    }
                }
            }
            catch {; }

            return packageFullName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        internal class PACKAGE_ID
        {
            public UInt32 reserved;
            public UInt32 processorArchitecture;
            public UInt64 version;
            public string name;
            public string publisher;
            public string resourceId;
            public string publisherId;
        };

        enum APPX_PACKAGE_ARCHITECTURE
        {
            APPX_PACKAGE_ARCHITECTURE_X86 = 0,
            APPX_PACKAGE_ARCHITECTURE_ARM = 5,
            APPX_PACKAGE_ARCHITECTURE_X64 = 9,
            APPX_PACKAGE_ARCHITECTURE_NEUTRAL = 11,
            APPX_PACKAGE_ARCHITECTURE_ARM64 = 12
        };

        // NOTE: These APIs are only available in Windows 8 and later, and If called on Windows 7 we expect an EntryPointNotFoundException will be thrown.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PackageFamilyNameFromId(PACKAGE_ID packageId, ref uint packageFullNameLength, StringBuilder packageFullName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PackageFullNameFromId(PACKAGE_ID packageId, ref uint packageFullNameLength, StringBuilder packageFullName);
    }
}
