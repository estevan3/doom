using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared NavMesh helpers. Used by WaveManager to place spawned enemies onto the
/// baked NavMesh before their agent is enabled, so SetDestination never throws
/// ("SetDestination can only be called on an active agent that has been placed on a NavMesh").
/// </summary>
public static class NavMeshUtil
{
    /// <summary>
    /// True when a runtime NavMesh is actually baked (has at least one triangle).
    /// Enabling a NavMeshAgent before this returns false can leave it unplaced, causing
    /// "SetDestination can only be called on an active agent that has been placed on a NavMesh".
    /// </summary>
    public static bool IsBaked()
    {
        return NavMesh.CalculateTriangulation().vertices.Length > 0;
    }

    /// <summary>
    /// Finds the nearest walkable position to <paramref name="position"/>. Returns false
    /// (with <paramref name="snapped"/> = input) when no NavMesh is baked or nothing is
    /// reachable within <paramref name="maxDistance"/>.
    /// </summary>
    public static bool TrySnapToNavMesh(Vector3 position, float maxDistance, out Vector3 snapped)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
        {
            snapped = hit.position;
            return true;
        }

        snapped = position;
        return false;
    }

    /// <summary>
    /// Non-failing variant of <see cref="TrySnapToNavMesh"/>: always returns a position,
    /// falling back to the input when no NavMesh sample is found.
    /// </summary>
    public static Vector3 SnapToNavMesh(Vector3 position, float maxDistance)
    {
        TrySnapToNavMesh(position, maxDistance, out Vector3 snapped);
        return snapped;
    }
}