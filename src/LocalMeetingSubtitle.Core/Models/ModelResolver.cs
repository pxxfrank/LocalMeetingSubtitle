namespace LocalMeetingSubtitle.Core.Models;

/// <summary>
/// Outcome of choosing which descriptor the live (loopback/microphone) recognizer should use.
/// <see cref="FallbackReason"/> is non-null when the stored choice could not be honoured, so the
/// caller can tell the user instead of silently recognizing with a different model.
/// </summary>
public sealed record ModelResolution(ModelDescriptor Descriptor, ModelDescriptor? Requested, string? FallbackReason);

/// <summary>
/// Pure selection/fallback policy for the recognition model. Installation state is supplied as a
/// predicate rather than an <c>IModelManager</c>, so the policy stays I/O-free and unit-testable.
///
/// A missing model must never be silent: a flattened or partially copied <c>models/</c> directory
/// once made the app report "not installed" and disable Start with no explanation.
/// </summary>
public static class ModelResolver
{
    /// <summary>
    /// Picks the descriptor to use. The stored id wins when it is selectable and installed; otherwise
    /// the first installed selectable model is used, and <paramref name="fallback"/> is the last
    /// resort so the caller always receives a non-null descriptor.
    /// </summary>
    public static ModelResolution Resolve(
        string? requestedId,
        IReadOnlyList<ModelDescriptor> selectable,
        ModelDescriptor fallback,
        Func<ModelDescriptor, bool> isInstalled)
    {
        var requested = string.IsNullOrWhiteSpace(requestedId)
            ? null
            : selectable.FirstOrDefault(m => string.Equals(m.Id, requestedId, StringComparison.OrdinalIgnoreCase));

        if (requested is not null && isInstalled(requested))
        {
            return new ModelResolution(requested, requested, null);
        }

        var chosen = selectable.FirstOrDefault(isInstalled) ?? fallback;

        string? reason = null;
        if (requested is not null)
        {
            reason = $"所选的识别模型未安装，已改用 {chosen.DisplayName} / "
                + $"the selected model is not installed; using {chosen.DisplayName}.";
        }
        else if (!isInstalled(chosen))
        {
            reason = "未安装任何识别模型，请在「设置 → 识别」中下载 / "
                + "no recognition model is installed — download one in Settings → Recognition.";
        }

        return new ModelResolution(chosen, requested, reason);
    }
}
