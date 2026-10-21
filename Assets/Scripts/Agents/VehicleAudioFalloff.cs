using UnityEngine;

/// <summary>Local vehicle sounds: clear nearby, faint at range, silent past the final key.</summary>
public static class VehicleAudioFalloff
{
    public static void Configure(AudioSource source, float near, float faint, float cutoff)
    {
        source.spatialBlend = 1;
        source.minDistance = near;
        source.maxDistance = cutoff;
        source.rolloffMode = AudioRolloffMode.Custom;
        // Unity's custom rolloff uses distance / maxDistance on the horizontal axis.
        // Flat tangents keep each transition smooth and prevent overshoot below zero.
        var curve = new AnimationCurve(
            new Keyframe(0, 1),
            new Keyframe(near / cutoff, 1),
            new Keyframe(near * 3 / cutoff, .3f),
            new Keyframe(faint / cutoff, .06f),
            new Keyframe(1, 0));
        curve.preWrapMode = curve.postWrapMode = WrapMode.ClampForever;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
    }
}
