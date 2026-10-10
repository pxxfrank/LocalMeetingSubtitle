namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// A speech region produced by <see cref="AudioSegmenter"/>, located on the stream that was fed
/// to the segmenter. Positions are absolute sample indices counted since the last <see cref="AudioSegmenter.Reset"/>,
/// so the caller maps them onto a media timeline (e.g. via <c>PcmBlock.Start</c>) at its own rate.
/// </summary>
/// <param name="Samples">Mono float samples of the region.</param>
/// <param name="StartSample">Inclusive start sample index.</param>
/// <param name="EndSample">Exclusive end sample index.</param>
/// <param name="HasOverlapPrefix">
/// True when the region begins with audio retained from the previous max-length cut, so the caller
/// must remove the duplicated text the model produces for that carry-over.
/// </param>
public readonly record struct SpeechSegment(
    float[] Samples,
    long StartSample,
    long EndSample,
    bool HasOverlapPrefix)
{
    public long Length => EndSample - StartSample;
}
