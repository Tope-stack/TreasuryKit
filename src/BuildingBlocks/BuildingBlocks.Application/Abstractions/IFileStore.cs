namespace TreasuryKit.BuildingBlocks.Application.Abstractions;

/// <summary>Blob storage for uploaded files. Local disk in development; S3-compatible later.</summary>
public interface IFileStore
{
    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/>, overwriting any existing file.</summary>
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
}
