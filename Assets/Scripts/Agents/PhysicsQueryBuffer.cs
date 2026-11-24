using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable main-thread physics results. Full buffers grow and retry so no hit is lost.</summary>
public sealed class PhysicsQueryBuffer
{
    RaycastHit[] hits = new RaycastHit[32];
    Collider[] colliders = new Collider[32];
    public RaycastHit[] Hits => hits;
    public Collider[] Colliders => colliders;
    public static readonly IComparer<RaycastHit> ByDistance = new HitDistanceComparer();

    sealed class HitDistanceComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    public int Raycast(Vector3 origin, Vector3 direction, float distance, int layers = ~0,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, layers, triggers);
            if (count < hits.Length) return count;
            Array.Resize(ref hits, hits.Length * 2);
        }
    }

    public int SphereCast(Vector3 origin, float radius, Vector3 direction, float distance, int layers = ~0,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, distance, layers, triggers);
            if (count < hits.Length) return count;
            Array.Resize(ref hits, hits.Length * 2);
        }
    }

    public int BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion rotation, float distance,
        int layers = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.BoxCastNonAlloc(center, halfExtents, direction, hits, rotation, distance, layers, triggers);
            if (count < hits.Length) return count;
            Array.Resize(ref hits, hits.Length * 2);
        }
    }

    public int OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, int layers = ~0,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, colliders, rotation, layers, triggers);
            if (count < colliders.Length) return count;
            Array.Resize(ref colliders, colliders.Length * 2);
        }
    }

    public int OverlapSphere(Vector3 center, float radius, int layers = ~0,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, colliders, layers, triggers);
            if (count < colliders.Length) return count;
            Array.Resize(ref colliders, colliders.Length * 2);
        }
    }

    public int CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance,
        int layers = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits, distance, layers, triggers);
            if (count < hits.Length) return count;
            Array.Resize(ref hits, hits.Length * 2);
        }
    }

    public int OverlapCapsule(Vector3 bottom, Vector3 top, float radius, int layers = ~0,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        while (true)
        {
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, colliders, layers, triggers);
            if (count < colliders.Length) return count;
            Array.Resize(ref colliders, colliders.Length * 2);
        }
    }
}
