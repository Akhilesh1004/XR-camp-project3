using UnityEngine;
using UnityEngine.Rendering;

public class DroneVisualOptimizer
{
    private Renderer[] renderers;
    private Animator[] animators;
    private MeshCollider[] meshColliders;
    private GameObject initializedOwner;
    private bool initialized;
    private bool visible = true;

    public bool IsVisible => visible;

    public void Initialize(
        GameObject owner,
        bool disableChildMeshColliders,
        bool optimizeRendererSettings
    )
    {
        if (owner == null)
        {
            return;
        }

        if (initialized && initializedOwner == owner)
        {
            return;
        }

        renderers = owner.GetComponentsInChildren<Renderer>(true);
        animators = owner.GetComponentsInChildren<Animator>(true);
        meshColliders = owner.GetComponentsInChildren<MeshCollider>(true);

        if (disableChildMeshColliders && meshColliders != null)
        {
            for (int i = 0; i < meshColliders.Length; i++)
            {
                MeshCollider meshCollider = meshColliders[i];

                if (meshCollider != null)
                {
                    meshCollider.enabled = false;
                }
            }
        }

        if (optimizeRendererSettings && renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        if (animators != null)
        {
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];

                if (animator != null)
                {
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
            }
        }

        initializedOwner = owner;
        initialized = true;
    }

    public void SetVisible(bool shouldBeVisible, bool disableAnimatorsWhenHidden)
    {
        if (!initialized || visible == shouldBeVisible)
        {
            return;
        }

        visible = shouldBeVisible;

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer != null)
                {
                    renderer.enabled = shouldBeVisible;
                }
            }
        }

        if (disableAnimatorsWhenHidden && animators != null)
        {
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];

                if (animator != null)
                {
                    animator.enabled = shouldBeVisible;
                }
            }
        }
    }

    public void ForceVisible(bool disableAnimatorsWhenHidden)
    {
        visible = false;
        SetVisible(true, disableAnimatorsWhenHidden);
    }
}
