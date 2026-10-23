using System.Text;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Services;

public static class CuiaSessionBudget
{
    // The model receives decoded text. Counting an escaped JSON envelope double-counts nested escapes.
    // UTF-8 bytes intentionally overestimate ordinary text token counts; keep output and framing margin.
    public static int Estimate(AiCompletionRequest request) => checked(
        Encoding.UTF8.GetByteCount(request.SystemInstruction) + Encoding.UTF8.GetByteCount(request.UserPayload)
        + request.MaxOutputTokens + 512);
}
