using UnityEngine;

/// <summary>Spatial driving recording follows the patrol's speed, including obstacle stops.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BaseRoadJeepPatrol))]
public sealed class JeepDrivingAudio : MonoBehaviour
{
    public AudioClip drivingClip;
    [Range(0, 1)] public float volume = .3f;
    BaseRoadJeepPatrol patrol;
    AudioSource source;

    void Awake()
    {
        patrol = GetComponent<BaseRoadJeepPatrol>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = drivingClip;
        source.spatialBlend = 1;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 5;
        source.maxDistance = 65;
        source.dopplerLevel = .15f;
        source.volume = 0;
    }

    void Update()
    {
        if (!drivingClip) return;
        float load = patrol.isActiveAndEnabled ? Mathf.Clamp01(patrol.CurrentSpeed / Mathf.Max(.1f, patrol.speed)) : 0;
        source.volume = Mathf.MoveTowards(source.volume, volume * load, Time.deltaTime * .45f);
        source.pitch = Mathf.Lerp(source.pitch, Mathf.Lerp(.85f, 1.12f, load), 1 - Mathf.Exp(-2 * Time.deltaTime));
        if (source.volume > .001f && !source.isPlaying) source.Play();
        if (source.volume <= .001f && source.isPlaying) source.Stop();
    }

    void OnDisable() { if (source) { source.Stop(); source.volume = 0; } }
}
