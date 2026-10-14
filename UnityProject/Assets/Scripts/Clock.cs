using UnityEngine;

// Real (unscaled) time for effects that must ignore hit-stop and slow motion - HUD timers, camera
// smoothing, hit-stop itself. While the showcase records at a fixed 30 frames per second this is
// the video's clock instead, so titles, subtitles and camera moves last as long in the video as
// they would on screen.
public static class Clock
{
    public static float Dt => Showcase.Active ? 1f / 30f : Time.unscaledDeltaTime;
    public static float Now => Showcase.Active ? Time.frameCount / 30f : Time.unscaledTime;
}
