using System;
using UnityEngine;

public enum EliteAction { Defend, Engage, Cover, Search, Return }

[Serializable]
public sealed class EliteUtility
{
    [Range(0, 2)] public float defendWeight = .45f, engageWeight = .9f, coverWeight = 1.15f, searchWeight = .65f, returnWeight = .85f;
    [Range(0, 1)] public float switchingMargin = .12f;
    [Min(0)] public float minimumCommitment = 1.1f;

    public float[] Scores(bool visible, bool memory, bool cover, bool home, bool inside, float health, float suitability, float confidence, float distance, float exposure)
    {
        health = Mathf.Clamp01(health); suitability = Mathf.Clamp01(suitability);
        confidence = Mathf.Clamp01(confidence); distance = Mathf.Clamp01(distance); exposure = Mathf.Clamp01(exposure);
        return new[] {
            home ? defendWeight * (visible ? .35f : 1) : -1,
            visible && inside ? engageWeight * (.45f + .35f * suitability + .2f * health) : -1,
            cover && memory && inside ? coverWeight * (.25f + .45f * (1 - health) + .3f * exposure) * confidence : -1,
            !visible && memory && inside ? searchWeight * confidence : -1,
            !home ? returnWeight * (.35f + .65f * distance + (!memory || !inside ? .65f : 0)) : -1
        };
    }

    public EliteAction Select(EliteAction current, float[] scores, float committedFor)
    {
        int best = 0;
        for (int i = 1; i < scores.Length; i++) if (scores[i] > scores[best]) best = i;
        if (scores[(int)current] < 0) return (EliteAction)best;
        if (committedFor < minimumCommitment || scores[best] <= scores[(int)current] + switchingMargin) return current;
        return (EliteAction)best;
    }
}
