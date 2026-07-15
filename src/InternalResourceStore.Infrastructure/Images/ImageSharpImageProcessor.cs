using InternalResourceStore.Application;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace InternalResourceStore.Infrastructure.Images;

public sealed class ImageSharpImageProcessor : IImageProcessor
{
    public async Task<ProcessedImage> ProcessAsync(Stream input, string declaredMimeType, CancellationToken cancellationToken)
    {
        var normalizedMimeType = declaredMimeType.Trim().ToLowerInvariant();
        if (normalizedMimeType is not ("image/png" or "image/jpeg"))
            throw new InvalidOperationException("Only PNG and JPEG images are supported.");

        try
        {
            input.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(input, cancellationToken);
            var output = new MemoryStream();

            if (normalizedMimeType == "image/png")
            {
                await image.SaveAsync(output, new PngEncoder(), cancellationToken);
                output.Position = 0;
                return new ProcessedImage(output, "image/png", output.Length, image.Width, image.Height, "png");
            }

            await image.SaveAsync(output, new JpegEncoder(), cancellationToken);
            output.Position = 0;
            return new ProcessedImage(output, "image/jpeg", output.Length, image.Width, image.Height, "jpg");
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidOperationException("Image signature is invalid or unsupported.", ex);
        }
        catch (InvalidImageContentException ex)
        {
            throw new InvalidOperationException("Image content is invalid.", ex);
        }
    }
}
