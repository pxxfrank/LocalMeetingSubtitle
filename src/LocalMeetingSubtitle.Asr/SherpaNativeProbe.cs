namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Confirms the sherpa-onnx native library (sherpa-onnx-c-api / onnxruntime) loaded and can be
/// queried. Used to distinguish "model missing" from "native DLL missing" at startup.
/// </summary>
public static class SherpaNativeProbe
{
    public static bool TryLoad(out string? version, out string? error)
    {
        version = null;
        error = null;
        try
        {
            version = SherpaOnnx.VersionInfo.Version;
            return !string.IsNullOrEmpty(version);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
