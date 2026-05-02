namespace DanteLogger.util;

public static class VersionUtil
{
    public static string GetVersion()
    {
        return ThisAssembly.Git.Tag.Length switch
        {
            > 0 => ThisAssembly.Git.Tag,
            0 when ThisAssembly.Git.Branch.StartsWith('v') => ThisAssembly.Git.Branch,
            _ => $"v{ThisAssembly.Git.SemVer.Major}.{ThisAssembly.Git.SemVer.Minor}.{ThisAssembly.Git.SemVer.Patch}"
        };
    }
}