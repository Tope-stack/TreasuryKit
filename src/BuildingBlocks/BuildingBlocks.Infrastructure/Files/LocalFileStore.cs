using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application.Abstractions;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Files;

public sealed class FileStoreOptions
{
    public const string SectionName = "FileStore";

    /// <summary>Root directory; relative paths resolve against the host's content root. Api and Worker must share it.</summary>
    [Required]
    public string RootPath { get; set; } = string.Empty;
}

internal sealed class LocalFileStore(IOptions<FileStoreOptions> options, IHostEnvironment environment) : IFileStore
{
    private readonly string _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file then move, so readers never see a partial file.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var file = File.Create(temp))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        return File.Exists(path)
            ? Task.FromResult<Stream>(File.OpenRead(path))
            : throw new FileNotFoundException($"Stored file '{key}' not found.", path);
    }

    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid file key '{key}'.", nameof(key));
        }

        return path;
    }
}
