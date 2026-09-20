using System.Reflection;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Rhino uses the assembly Guid as the plug-in ID. It MUST be present and non-empty.
[assembly: Guid("8C9B3F6E-4E7A-4F2B-9B6E-1C3D2A5F7B10")]

// Metadata shown in Rhino's PluginManager.
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "United States")]
[assembly: PlugInDescription(DescriptionType.Email, "dave@cadalog-inc.com")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Cadalog, Inc.")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://www.cadalog-inc.com/")]

[assembly: AssemblyTitle("Podium Browser for Rhino")]
[assembly: AssemblyDescription("Browse the Podium Browser library inside Rhino and drop Rhino (.3dm) or SketchUp (.skp) assets straight into your model.")]
[assembly: AssemblyCompany("Cadalog, Inc.")]
[assembly: AssemblyProduct("Podium Browser for Rhino")]

// Yak reads AssemblyInformationalVersion (SemVer) for the package version,
// falling back to AssemblyVersion. Keep these in sync with <Version> in the .csproj.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
