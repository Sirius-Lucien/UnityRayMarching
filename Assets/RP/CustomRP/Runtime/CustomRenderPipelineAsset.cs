using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "CustomRenderPipeline", menuName = "Rendering/Custom Render Pipeline")]
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline();
    }
}
