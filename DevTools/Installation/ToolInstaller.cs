using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Linq;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DevTools.Infrastructure;

namespace DevTools.Installation;

public class ToolInstaller(IStorage storage, IVersionSource versionSource, IDownloader downloader)
    : IToolInstaller
{
    public Task<List<RemoteVersion>> GetVersionsAsync(string directory)
    {
        return versionSource.GetVersionsAsync(LoadSource(directory).Versions);
    }

    public async Task<string> InstallAsync(string directory, RemoteVersion version)
    {
        var source = LoadSource(directory);
        var target = Path.Combine(directory, version.Version);
        var work = Path.Combine(directory, $".install-{version.Version}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(work);
        try
        {
            string lastDownload = null;

            foreach (var step in source.Steps)
            {
                switch (step)
                {
                    case DownloadStep download:
                        lastDownload = await DownloadAsync(download, version, work);
                        break;
                    case ExtractStep extract:
                        Extract(extract, version, work, lastDownload);
                        lastDownload = null;
                        break;
                    case MoveStep move:
                        Move(move, version, work);
                        break;
                }
            }

            Directory.Move(work, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(work))
                Directory.Delete(work, true);
        }
    }

    public void Remove(string versionDirectory)
    {
        var work = Path.Combine(Path.GetDirectoryName(versionDirectory), $".remove-{Path.GetFileName(versionDirectory)}-{Guid.NewGuid():N}");

        // Moving fails while any file is in use, so a running tool stays complete instead of being half deleted.
        try
        {
            Directory.Move(versionDirectory, work);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot remove `{versionDirectory}`, nothing was deleted. Is it in use? {e.Message}", e);
        }

        try
        {
            DeleteTree(new DirectoryInfo(work));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Version is no longer available, but `{work}` could not be fully deleted: {e.Message}", e);
        }
    }

    // Directory.Delete(path, true) needs extra privileges to unmount junctions, and a link must go without touching its target.
    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                entry.Delete();
            else if (entry is DirectoryInfo child)
                DeleteTree(child);
            else
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }

        directory.Attributes &= ~FileAttributes.ReadOnly;
        directory.Delete();
    }

    private ToolSource LoadSource(string directory)
    {
        var fileName = Path.Combine(directory, ToolSource.FileName);
        var source = storage.Load<ToolSource>(fileName);

        if (source.Versions == null)
            throw new InvalidOperationException($"`{fileName}` does not define versions");

        if (source.Steps == null || source.Steps.Count == 0)
            throw new InvalidOperationException($"`{fileName}` does not define steps");

        return source;
    }

    private async Task<string> DownloadAsync(DownloadStep step, RemoteVersion version, string work)
    {
        if ((step.Asset == null) == (step.Url == null))
            throw new InvalidOperationException("A download step needs either an asset or a url");

        var (url, name, digest) = step.Asset != null ? FindAsset(step, version) : (Expand(step.Url, version), (string)null, (string)null);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Only https downloads are supported: `{url}`");

        var relative = step.To != null ? Expand(step.To, version) : name ?? Path.GetFileName(uri.AbsolutePath);
        var fileName = GetSafePath(work, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(fileName));
        Console.WriteLine($"Downloading {url}");
        await downloader.DownloadAsync(url, fileName);
        VerifyDigest(fileName, digest);

        return fileName;
    }

    private static (string Url, string Name, string Digest) FindAsset(DownloadStep step, RemoteVersion version)
    {
        var name = Expand(step.Asset, version);
        var assets = version.Assets.Where(i => FileSystemName.MatchesSimpleExpression(name, i.Name, false)).ToList();

        if (assets.Count == 0)
            throw new InvalidOperationException($"Version {version.Version} has no asset `{name}`. Available: {version.Assets.Select(i => i.Name).Join(", ")}");

        if (assets.Count > 1)
            throw new InvalidOperationException($"Asset `{name}` of version {version.Version} is ambiguous: {assets.Select(i => i.Name).Join(", ")}");

        return (assets[0].Url, assets[0].Name, assets[0].Digest);
    }

    private static void VerifyDigest(string fileName, string digest)
    {
        const string prefix = "sha256:";

        if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        using var file = File.OpenRead(fileName);
        var actual = Convert.ToHexString(SHA256.HashData(file));

        if (!actual.Equals(digest[prefix.Length..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SHA-256 of `{Path.GetFileName(fileName)}` does not match: expected {digest[prefix.Length..]}, got {actual}");
    }

    private static void Extract(ExtractStep step, RemoteVersion version, string work, string lastDownload)
    {
        var archive = step.File != null ? GetSafePath(work, Expand(step.File, version)) : lastDownload;
        if (archive == null)
            throw new InvalidOperationException("An extract step needs a file or a preceding download step");

        var destination = Path.Combine(work, $".extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);

        Console.WriteLine($"Extracting {Path.GetFileName(archive)}");
        ExtractArchive(archive, destination);
        File.Delete(archive);

        var contentRoot = step.StripRoot ? GetSingleRoot(destination, archive) : destination;

        foreach (var entry in new DirectoryInfo(contentRoot).EnumerateFileSystemInfos())
            MoveEntry(entry.FullName, Path.Combine(work, entry.Name));

        Directory.Delete(destination, true);
    }

    private static void ExtractArchive(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination);
            return;
        }

        if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destination, false);
            return;
        }

        throw new InvalidOperationException($"Unsupported archive `{Path.GetFileName(archive)}`, expected .zip, .tar.gz or .tgz");
    }

    private static string GetSingleRoot(string destination, string archive)
    {
        var entries = new DirectoryInfo(destination).GetFileSystemInfos();

        if (entries.Length != 1 || entries[0] is not DirectoryInfo)
            throw new InvalidOperationException($"`{Path.GetFileName(archive)}` does not have a single root directory, stripRoot cannot be used");

        return entries[0].FullName;
    }

    private static void Move(MoveStep step, RemoteVersion version, string work)
    {
        if (step.From == null || step.To == null)
            throw new InvalidOperationException("A move step needs from and to");

        var from = GetSafePath(work, Expand(step.From, version));
        var to = GetSafePath(work, Expand(step.To, version));

        Directory.CreateDirectory(Path.GetDirectoryName(to));
        MoveEntry(from, to);
    }

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    private static string GetSafePath(string work, string relative)
    {
        var root = Path.GetFullPath(work);
        var path = Path.GetFullPath(Path.Combine(root, relative));

        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path `{relative}` is outside of the installation directory");

        return path;
    }

    private static string Expand(string value, RemoteVersion version)
    {
        return value.Replace("{version}", version.Version).Replace("{tag}", version.Tag);
    }
}