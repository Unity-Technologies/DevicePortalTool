using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace DevicePortalTool
{
    public class Dependency
    {
        public string Name { get; private set; }
        public Version MinVersion { get; private set; }

        public Dependency(string name, string minVersion)
        {
            Name = name;
            MinVersion = new Version(minVersion);
        }
    }

    public class AppxManifest
    {
        private static Dictionary<string, AppxManifest> ManifestCache = new Dictionary<string, AppxManifest>();

        public string AppxPath { get; private set; }
        public Guid PhoneProductId { get; private set; }
        public string PackageName { get; private set; }
        public string Publisher { get; private set; }
        public string Version { get; private set; }
        public string AppId { get; private set; }
        public List<string> Dependencies { get; private set; }
        public string CpuArchitecture { get; private set; }
        public string ResourceId { get; private set; }
        public bool IsFramework { get; private set; }
        public bool IsDependency { get; private set; }
        public bool IsValid { get; private set; }

        public static AppxManifest Get(string appxPath)
        {
            if (!ManifestCache.ContainsKey(appxPath))
            {
                return new AppxManifest(appxPath);      // Constructor will add it to manifest cache midway through construction.
            }

            return ManifestCache[appxPath];
        }

        public static AppxManifest Get(string appxPath, string extractedPath)
        {
            if (!ManifestCache.ContainsKey(appxPath))
            {
                return new AppxManifest(appxPath, extractedPath);       // Constructor will add it to manifest cache midway through construction.
            }

            return ManifestCache[appxPath];
        }

        private AppxManifest(string appxPath)
        {
            AppxPath = appxPath;
            var extension = Path.GetExtension(appxPath);
            if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
            {
                Parse(XDocument.Load(appxPath));
            }
            else
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(appxPath))
                {
                    var manifestStream = archive.GetEntry("AppxManifest.xml").Open();
                    Parse(XDocument.Load(manifestStream));
                }
            }
        }

        private AppxManifest(string appxPath, string extractedPath)
        {
            AppxPath = appxPath;
            using (var stream = new StreamReader(Path.Combine(extractedPath, "AppXManifest.xml")))
            {
                Parse(XDocument.Load(stream));
            }
        }

        private void Parse(XDocument manifest)
        {
            ManifestCache.Add(AppxPath, this);

            string namezspace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            try
            {
                var phoneIdentity = manifest.Root.Element(XName.Get("PhoneIdentity", "http://schemas.microsoft.com/appx/2014/phone/manifest"));
                if (phoneIdentity != null)
                {
                    PhoneProductId = Guid.Parse(phoneIdentity.Attribute("PhoneProductId").Value);
                }

                var identity = manifest.Root.Element(XName.Get("Identity", namezspace));
                if (identity == null)
                {
                    throw new ArgumentNullException("identity");
                }

                PackageName = identity.Attribute("Name")?.Value;
                if (PackageName == null)
                {
                    throw new ArgumentNullException("packageName");
                }

                Publisher = identity.Attribute("Publisher")?.Value;
                if (Publisher == null)
                {
                    throw new ArgumentNullException("publisher");
                }

                Version = identity.Attribute("Version")?.Value;
                if (Version == null)
                {
                    throw new ArgumentNullException("version");
                }

                var applications = manifest.Root.Element(XName.Get("Applications", namezspace));
                if (applications != null)
                {
                    var applicationElement = applications.Element(XName.Get("Application", namezspace));

                    if (applicationElement != null)
                    {
                        AppId = applicationElement.Attribute(XName.Get("Id"))?.Value;
                        if (AppId == null)
                        {
                            throw new ArgumentNullException("appid");
                        }
                    }
                }

                // Optional identity attributes
                CpuArchitecture = identity.Attribute("ProcessorArchitecture")?.Value ?? "neutral";
                ResourceId = identity.Attribute("ResourceId")?.Value ?? "";

                var properties = manifest.Root.Element(XName.Get("Properties", namezspace));
                var frameworkAttribute = properties.Element(XName.Get("Framework", namezspace));

                IsFramework = frameworkAttribute != null && frameworkAttribute.Value.Equals("True", StringComparison.InvariantCultureIgnoreCase);
            }
            catch   // This will happen if we happen to try to parse non-UWP app manifest
            {
                IsValid = false;
                return;
            }

            IsValid = true;

            var dependencies = manifest.Root.Element(XName.Get("Dependencies", namezspace));

            Dependencies = new List<string>();
            if (dependencies != null)
            {
                foreach (var dependencyInfo in dependencies.Descendants())
                {
                    var dependency = new Dependency(dependencyInfo.Attribute("Name").Value, dependencyInfo.Attribute("MinVersion").Value);
                    AddDependency(dependency, AppxPath);
                }
            }
        }

        private void AddDependency(Dependency dependency, string appxPath)
        {
            var appxFolder = Path.GetDirectoryName(appxPath);
            var dependenciesFolder = Path.Combine(appxFolder, "Dependencies");
            var searchFolders = new string[] { Path.Combine(dependenciesFolder, CpuArchitecture), dependenciesFolder, appxFolder };

            foreach (var searchFolder in searchFolders)
            {
                if (!Directory.Exists(searchFolder))
                    continue;

                foreach (var file in Directory.GetFiles(searchFolder, "*.appx"))
                {
                    if (AddDependenciesIfMatches(file, dependency))
                    {
                        return;
                    }
                }
            }
        }

        private bool AddDependenciesIfMatches(string path, Dependency dependency)
        {
            var dependencyManifest = Get(path);

            if (dependencyManifest.IsValid && dependencyManifest.PackageName.Equals(dependency.Name, StringComparison.InvariantCultureIgnoreCase))
            {
                Dependencies.AddRange(dependencyManifest.Dependencies);
                Dependencies.Add(path);
                dependencyManifest.IsDependency = true;
                return true;
            }

            return false;
        }
    }
}
