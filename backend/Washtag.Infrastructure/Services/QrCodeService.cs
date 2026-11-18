using QRCoder;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

/// <summary>Generates QR receipts in .NET with the QRCoder library.</summary>
public class QrCodeService : IQrCodeService
{
    public string GenerateDataUrl(string payload) =>
        $"data:image/png;base64,{Convert.ToBase64String(GeneratePng(payload))}";

    public byte[] GeneratePng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(20);
    }
}
