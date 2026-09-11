using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class NavMeshSetup : MonoBehaviour
{
    [Header("Bake Settings")]
    public float bakeDelay = 0.5f;
    public bool bakeOnAwake = true;

    private NavMeshSurface surface;

    void Awake()
    {
        if (bakeOnAwake)
        {
            Bake();
        }
    }

    public void Bake()
    {
        if (surface == null)
        {
            surface = GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = gameObject.AddComponent<NavMeshSurface>();
            }
        }

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }
}