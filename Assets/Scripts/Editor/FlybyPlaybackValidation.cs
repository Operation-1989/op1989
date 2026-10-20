using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks real audio playback through two complete recordings, even with slow game time.</summary>
public static class FlybyPlaybackValidation
{
    const string Active = "FlybyPlaybackValidation";
    const string Report = "Temp/MissionAudio/flyby-playmode.txt";
    static bool initialized;
    static double deadline, started;
    static AudioClip previous;
    static int passes;
    static AudioSource source;
    static MissionAmbientAudio audio;

    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (SessionState.GetBool(Active, false))
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayStateChanged;
        }
    }

    static void PlayStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) SessionState.SetBool(Active + "Ready", true);
    }

    [MenuItem("Operation 1989/Audio/Validate Full Flyby Playback")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= RunAfterExit;
            EditorApplication.playModeStateChanged += RunAfterExit;
            EditorApplication.isPlaying = false;
            return;
        }
        MissionAudioSetup.ConfigureFlybys();
        Directory.CreateDirectory("Temp/MissionAudio");
        File.WriteAllText(Report, "Full flyby playback checks (native audio clock)\n");
        SessionState.SetBool(Active, true);
        SessionState.SetBool(Active + "Ready", false);
        initialized = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayStateChanged;
        EditorApplication.playModeStateChanged += PlayStateChanged;
        EditorApplication.isPlaying = true;
    }

    static void RunAfterExit(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RunAfterExit;
        Run();
    }

    static void Check(string name, bool pass) => File.AppendAllText(Report, (pass ? "PASS " : "FAIL ") + name + "\n");

    public static void StopAndReport()
    {
        var current = Object.FindAnyObjectByType<MissionAmbientAudio>();
        var child = current ? current.transform.Find("Distant aircraft pass") : null;
        var playback = child ? child.GetComponent<AudioSource>() : null;
        File.WriteAllText("Temp/MissionAudio/playback-state.txt", "playing=" + EditorApplication.isPlaying +
            " paused=" + EditorApplication.isPaused + " dsp=" + AudioSettings.dspTime + " time=" + Time.time +
            " scale=" + Time.timeScale + " background=" + Application.runInBackground + " listenerPaused=" + AudioListener.pause +
            " enabled=" + (current && current.enabled) + " source=" + (bool)playback +
            (playback ? " clip=" + playback.clip + " playing=" + playback.isPlaying + " seconds=" + playback.time + " muted=" + playback.mute : ""));
        if (SessionState.GetBool(Active, false)) Finish();
    }

    static void Finish()
    {
        Time.timeScale = SessionState.GetFloat(Active + "Scale", 1);
        Application.runInBackground = SessionState.GetBool(Active + "Background", false);
        SessionState.SetBool(Active, false);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayStateChanged;
        EditorApplication.isPlaying = false;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Active + "Ready", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (!initialized)
            {
                audio = Object.FindAnyObjectByType<MissionAmbientAudio>();
                if (!audio) return;
                var child = audio.transform.Find("Distant aircraft pass");
                if (!child) return;
                source = child.GetComponent<AudioSource>();
                SessionState.SetFloat(Active + "Scale", Time.timeScale);
                SessionState.SetBool(Active + "Background", Application.runInBackground);
                Time.timeScale = .1f;
                Application.runInBackground = true;
                // Audio listeners remain active; skip expensive scene rendering during this audio-only check.
                foreach (var camera in Object.FindObjectsByType<Camera>()) camera.enabled = false;
                // Keep this playback check outside and free from combat, without changing saved scene data.
                foreach (var guard in Object.FindObjectsByType<BarricadePatrolSoldier>()) guard.enabled = false;
                var movement = audio.player.GetComponent<FirstPersonController>();
                if (movement) movement.enabled = false;
                audio.enabled = false;
                audio.enabled = true;
                previous = null;
                passes = 0;
                deadline = AudioSettings.dspTime + 150;
                initialized = true;
            }
            if (AudioSettings.dspTime > deadline)
            {
                Check("Plane and helicopter both complete within 150 audio seconds", false);
                Finish();
                return;
            }
            if (!source.isPlaying || source.clip == previous) return;
            double now = AudioSettings.dspTime;
            var pcm = new float[4096];
            float peak = 0;
            for (int sample = 0; sample < source.clip.samples; sample += source.clip.frequency)
                if (source.clip.GetData(pcm, sample))
                    foreach (float value in pcm) peak = Mathf.Max(peak, Mathf.Abs(value));
            Check(source.clip.name + " contains audible PCM audio", peak > .001f);
            File.AppendAllText(Report, "Playing " + source.clip.name + " peak=" + peak.ToString("F3") + " volume=" + source.volume + "\n");
            if (passes == 0)
            {
                Check("First pass starts during intro", audio.intro && !audio.intro.HandedOff);
                Check("First pass is plane", source.clip == audio.flybyClips[0]);
                Check("Plane has no fade or distance attenuation", source.spatialBlend == 0 && Mathf.Approximately(source.volume, audio.flybyVolume) && !source.mute);
                Check("No randomized pitch or looping", Mathf.Approximately(source.pitch, 1) && !source.loop);
            }
            else
            {
                double elapsed = now - started;
                Check(previous.name + " plays its full duration", elapsed >= previous.length);
                Check("Next pass respects the configured gap", elapsed >= previous.length + audio.flybyInterval.x - .5f && elapsed <= previous.length + audio.flybyInterval.y + 2);
                Check("Plane and helicopter alternate", source.clip == audio.flybyClips[passes % audio.flybyClips.Length]);
                Check("Playback continues with slow game time", Mathf.Approximately(Time.timeScale, .1f) && source.timeSamples >= 0);
                File.AppendAllText(Report, "Previous clip=" + previous.name + " duration=" + previous.length + " next start after=" + elapsed.ToString("F2") + " audio seconds\n");
            }
            previous = source.clip;
            started = now;
            passes++;
            if (passes >= 3) Finish();
        }
        catch (Exception e)
        {
            Check(e.ToString(), false);
            Finish();
        }
    }
}
