#if NETFX
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Sentry.PlatformAbstractions
{
    /// <summary>
    /// Information about .NET Framework in the running machine
    /// </summary>
    public static partial class FrameworkInfo
    {

        /// <summary>
        /// Get the latest Framework installation for the specified CLR
        /// </summary>
        /// <remarks>
        /// Supports the current 3 CLR versions:
        /// CLR 1 => .NET 1.0, 1.1
        /// CLR 2 => .NET 2.0, 3.0, 3.5
        /// CLR 4 => .NET 4.0, 4.5.x, 4.6.x, 4.7.x
        /// </remarks>
        /// <param name="clrVersion">The CLR version: 1, 2 or 4</param>
        /// <returns>The framework installation or null if none is found.</returns>
        public static FrameworkInstallation GetLatest(int clrVersion)
        {
            // CLR versions
            // https://docs.microsoft.com/en-us/dotnet/standard/clr
            if (clrVersion != 1 && clrVersion != 2 && clrVersion != 4)
            {
                return null;
            }

#if NET45PLUS
            if (clrVersion == 4)
            {
                var release = Get45PlusLatestInstallationFromRegistry();
                if (release != null)
                {
                    return new FrameworkInstallation
                    {
                        Version = GetNetFxVersionFromRelease(release.Value),
                        Release = release
                    };
                }
            }
#endif
            FrameworkInstallation latest = null;
            foreach (var installation in GetInstallations())
            {
                if (latest == null)
                {
                    latest = installation;
                }

                if (clrVersion == 2)
                {
                    // CLR 2 runs .NET 2 to 3.5
                    if ((installation.Version.Major == 2 || installation.Version.Major == 3)
                        && installation.Version >= latest.Version)
                    {
                        latest = installation;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (clrVersion == 4)
                {
                    if (installation.Version.Major == 4
                        && installation.Version >= latest.Version)
                    {
                        latest = installation;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return latest;
        }

        /// <summary>
        /// Get all .NET Framework installations in this machine
        /// </summary>
        /// <seealso href="https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#to-find-net-framework-versions-by-querying-the-registry-in-code-net-framework-1-4"/>
        /// <returns>Enumeration of installations</returns>
        public static IEnumerable<FrameworkInstallation> GetInstallations()
        {
            using (var ndpKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, string.Empty)
                .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\"))
            {
                if (ndpKey == null)
                {
                    yield break;
                }

                foreach (var versionKeyName in ndpKey.GetSubKeyNames())
                {
                    if (!versionKeyName.StartsWith("v") || !(ndpKey.OpenSubKey(versionKeyName) is RegistryKey versionKey))
                    {
                        continue;
                    }

                    var version = versionKey.GetString("Version");
                    if (version != null && versionKey.GetInt("Install") == 1)
                    {
                        // 1.0 to 3.5
                        yield return new FrameworkInstallation
                        {
                            ShortName = versionKeyName,
                            Version = ParseOrNull(version),
                            ServicePack = versionKey.GetInt("SP")
                        };

                        continue;
                    }

                    // 4.0+
                    foreach (var subKeyName in versionKey.GetSubKeyNames())
                    {
                        var subKey = versionKey.OpenSubKey(subKeyName);
                        if (subKey?.GetInt("Install") != 1)
                        {
                            continue;
                        }

                        yield return GetFromV4(subKey, subKeyName);
                    }
                }
            }
        }

        private static FrameworkInstallation GetFromV4(RegistryKey subKey, string subKeyName)
        {
            var hasRelease = int.TryParse(
                subKey.GetValue("Release", null)?.ToString(), out var release);

            Version version = null;
            if (hasRelease)
            {
                // 4.5+
                var displayableVersion = GetNetFxVersionFromRelease(release);
                if (displayableVersion != null)
                {
                    version = displayableVersion;
                }
            }

            if (version == null)
            {
                version = ParseOrNull(subKey.GetString("Version"));
            }

            FrameworkProfile? profile = null;
            switch (subKeyName)
            {
                case "Full":
                    profile = FrameworkProfile.Full;
                    break;
                case "Client":
                    profile = FrameworkProfile.Client;
                    break;
            }

            return new FrameworkInstallation
            {
                Profile = profile,
                Version = version,
                ServicePack = subKey.GetInt("SP"),
                Release = hasRelease ? release : null as int?
            };
        }

#if NET45PLUS
        // https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#to-find-net-framework-versions-by-querying-the-registry-in-code-net-framework-45-and-later
        internal static int? Get45PlusLatestInstallationFromRegistry()
        {
            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
            {
                return ndpKey?.GetInt("Release");
            }
        }
#endif

        internal static Version GetNetFxVersionFromRelease(int release)
        {
            NetFxReleaseVersionMap.TryGetValue(release, out var version);
            return ParseOrNull(version);
        }

        private static Version ParseOrNull(string version)
        {
#if NET35
            try
            {
                return new Version(version);
            }
            catch
            {
                return null;
            }
#else
            Version.TryParse(version, out var parsed);
            return parsed;
#endif
        }
    }
}

#endif
